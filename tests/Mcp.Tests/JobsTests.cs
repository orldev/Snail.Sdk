using System.IO.Pipelines;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>A long operation as a job: answered within the wait or handed back running, read as a resource, watched by a subscriber.</summary>
public sealed class JobsTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Job_ThatEndsWithinTheWait_IsAnsweredWithItsResult()
    {
        var jobs = new Jobs("sample");

        var job = jobs.Start("echo", async (progress, token) =>
        {
            progress.Report(new JobProgress(1, 2, "half"));
            await Task.Delay(20, token);

            return new Outcome("done");
        }, SampleJson.Default.Outcome);
        var report = await job.ForAsync(Patience);

        Assert.Equal("job://sample/echo/1", job.Uri);
        Assert.Equal(JobState.Succeeded, report.State);
        Assert.Equal(1, report.Progress);
        Assert.Equal("half", report.Message);
        Assert.NotNull(report.EndedAt);
        Assert.Equal("done", report.Result!.Value.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Job_StillRunningPastTheWait_IsAnsweredRunning_AndEndsOnItsOwn()
    {
        var jobs = new Jobs("sample");
        var release = new TaskCompletionSource();

        var job = jobs.Start("slow", async (_, token) =>
        {
            await release.Task.WaitAsync(token);

            return new Outcome("late");
        }, SampleJson.Default.Outcome);
        var running = await job.ForAsync(TimeSpan.FromMilliseconds(50));

        Assert.Equal(JobState.Running, running.State);
        Assert.Null(running.EndedAt);
        Assert.Same(job, jobs.Find(job.Uri));
        Assert.Single(jobs.InFlight);

        release.SetResult();
        var ended = await job.Completion.WaitAsync(Patience);

        Assert.Equal(JobState.Succeeded, ended.State);
        Assert.Empty(jobs.InFlight);
        Assert.Single(jobs.Recent);
    }

    [Fact]
    public async Task Job_ThatThrows_FailsWithTheMessage_AndACancelledOne_IsCancelled()
    {
        var jobs = new Jobs("sample");

        var failing = jobs.Start("bad", (_, _) => Task.FromException<Outcome>(new InvalidOperationException("no scene")), SampleJson.Default.Outcome);
        var cancelled = jobs.Start("long", async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return new Outcome("never");
        }, SampleJson.Default.Outcome);

        cancelled.Cancel();
        var failed = await failing.Completion.WaitAsync(Patience);
        var stopped = await cancelled.Completion.WaitAsync(Patience);

        Assert.Equal(JobState.Failed, failed.State);
        Assert.Equal("no scene", failed.Message);
        Assert.Equal(JobState.Cancelled, stopped.State);
        Assert.Null(stopped.Result);
    }

    /// <summary>A stdio server exits with its client: a job still running when the host stops is cancelled, not left behind.</summary>
    [Fact]
    public async Task Job_RunningWhenTheHostStops_IsCancelled()
    {
        await using var stand = await Stand.StartAsync();
        var jobs = stand.Host.Services.GetRequiredService<Jobs>();
        var job = jobs.Start("long", async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return new Outcome("never");
        }, SampleJson.Default.Outcome);

        await stand.Host.StopAsync();
        var ended = await job.Completion.WaitAsync(Patience);

        Assert.Equal(JobState.Cancelled, ended.State);
    }

    [Fact]
    public async Task Job_ReadAsAResource_AnswersItsReport_AndTheTemplateIsListed()
    {
        await using var stand = await Stand.StartAsync();
        var jobs = stand.Host.Services.GetRequiredService<Jobs>();
        var job = jobs.Start("echo", (_, _) => Task.FromResult(new Outcome("read me")), SampleJson.Default.Outcome);
        await job.Completion.WaitAsync(Patience);

        var read = await stand.Client.ReadResourceAsync(job.Uri);
        var templates = await stand.Client.ListResourceTemplatesAsync();
        var text = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
        var report = JobReport.FromJson(text.Text);

        Assert.True(stand.Client.ServerCapabilities.Resources?.Subscribe);
        Assert.Equal("application/json", text.MimeType);
        Assert.NotNull(report);
        Assert.Equal(JobState.Succeeded, report.State);
        Assert.Equal("read me", report.Result!.Value.GetProperty("text").GetString());
        Assert.Contains(templates, template => template.UriTemplate == "job://sample/{kind}/{id}");
    }

    [Fact]
    public async Task Job_AnUnknownUri_IsRefusedAsUnknown()
    {
        await using var stand = await Stand.StartAsync();

        var refused = await Assert.ThrowsAnyAsync<McpException>(async () => await stand.Client.ReadResourceAsync("job://sample/echo/404"));

        Assert.Contains("job://sample/echo/404", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>A subscriber is told of the end rather than asking: the notification arrives, and the report read after it is the final one.</summary>
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Job_Subscribed_TellsTheClientWhenItEnds(string protocolVersion)
    {
        await using var stand = await Stand.StartAsync(protocolVersion);
        var jobs = stand.Host.Services.GetRequiredService<Jobs>();
        var release = new TaskCompletionSource();
        var told = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = jobs.Start("slow", async (_, token) =>
        {
            await release.Task.WaitAsync(token);

            return new Outcome("told");
        }, SampleJson.Default.Outcome);

        await using var subscription = await stand.SubscribeAsync(job.Uri, uri => told.TrySetResult(uri));
        var watchers = stand.Host.Services.GetRequiredService<JobWatchers>();
        await Until(() => watchers.WatchersOf(job.Uri) == 1);

        Assert.Equal(1, watchers.WatchersOf(job.Uri));

        release.SetResult();
        var uri = await told.Task.WaitAsync(Patience);
        var read = await stand.Client.ReadResourceAsync(uri);
        var report = JobReport.FromJson(Assert.IsType<TextResourceContents>(Assert.Single(read.Contents)).Text);

        Assert.Equal(job.Uri, uri);
        Assert.Equal(JobState.Succeeded, report!.State);

        await subscription.DisposeAsync();
        await Until(() => watchers.WatchersOf(job.Uri) == 0);

        Assert.Equal(0, watchers.WatchersOf(job.Uri));
    }

    /// <summary>Progress steps reach a subscriber no more than once a second; the end reaches it at once.</summary>
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task Job_Progress_ReachesASubscriberThrottled(string protocolVersion)
    {
        await using var stand = await Stand.StartAsync(protocolVersion);
        var jobs = stand.Host.Services.GetRequiredService<Jobs>();
        var notified = 0;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource();
        var job = jobs.Start("steps", async (progress, token) =>
        {
            await started.Task.WaitAsync(token);

            for (var step = 1; step <= 50; step++)
                progress.Report(new JobProgress(step, 50));

            return new Outcome("stepped");
        }, SampleJson.Default.Outcome);

        await using var subscription = await stand.SubscribeAsync(job.Uri, _ =>
        {
            if (Interlocked.Increment(ref notified) > 0 && job.Report.State != JobState.Running)
                ended.TrySetResult();
        });
        var watchers = stand.Host.Services.GetRequiredService<JobWatchers>();
        await Until(() => watchers.WatchersOf(job.Uri) == 1);
        started.SetResult();
        await job.Completion.WaitAsync(Patience);
        await ended.Task.WaitAsync(Patience);

        Assert.InRange(notified, 1, 3);
    }

    /// <summary>An unsubscribe is a request the client answers before the server has taken it in: the test waits for the server.</summary>
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (!condition() && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
    }

    /// <summary>The sample server: a host with jobs over a pair of pipes, and the client connected to it.</summary>
    private sealed class Stand : IAsyncDisposable
    {
        private Stand(IHost host, McpClient client)
        {
            Host = host;
            Client = client;
        }

        public IHost Host { get; }

        public McpClient Client { get; }

        public static async Task<Stand> StartAsync(string? protocolVersion = null)
        {
            var toServer = new Pipe();
            var toClient = new Pipe();
            var builder = McpHost.CreateBuilder([]);

            builder.Services.AddMcpServer().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()).WithJobs("sample");

            var host = builder.Build();
            await host.StartAsync();
            var options = new McpClientOptions { ProtocolVersion = protocolVersion };
            var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), options);

            return new Stand(host, client);
        }

        /// <summary>
        /// Watches a resource the way the negotiated revision allows: <c>resources/subscribe</c> up to 2025-11-25, and from
        /// 2026-07-28 a <c>subscriptions/listen</c> request held open, on whose stream the updates come tagged with its id.
        /// </summary>
        /// <remarks>
        /// The listen request carries an id of the client's own: the SDK numbers a request inside SendRequestAsync without
        /// writing the number back, so a cancellation named after the request's id would name none, and cancelling the
        /// token alone sends the server nothing. What ends the stream on the server is <c>notifications/cancelled</c> with
        /// the id it was opened under.
        /// </remarks>
        public async Task<IAsyncDisposable> SubscribeAsync(string uri, Action<string> updated)
        {
            if (!string.Equals(Client.NegotiatedProtocolVersion, "2026-07-28", StringComparison.Ordinal))
                return await Client.SubscribeToResourceAsync(uri, (parameters, _) =>
                {
                    updated(parameters.Uri ?? string.Empty);

                    return ValueTask.CompletedTask;
                });

            var listening = new CancellationTokenSource();
            var handler = Client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification, (notification, _) =>
            {
                if (notification.Params?["uri"]?.GetValue<string>() is { } changed)
                    updated(changed);

                return ValueTask.CompletedTask;
            });
            var request = new JsonRpcRequest
            {
                Id = new RequestId($"listen-{Guid.NewGuid():N}"),
                Method = RequestMethods.SubscriptionsListen,
                Params = new JsonObject { ["notifications"] = new JsonObject { ["resourceSubscriptions"] = new JsonArray(uri) } },
            };
            var held = Client.SendRequestAsync(request, listening.Token);

            return new Listening(handler, listening, held, () =>
                Client.SendNotificationAsync(NotificationMethods.CancelledNotification, new CancelledNotificationParams { RequestId = request.Id }, McpJsonUtilities.DefaultOptions, CancellationToken.None));
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Host.StopAsync();
            Host.Dispose();
        }

        /// <summary>Ends the listen request and its handler; disposed twice — by hand and by the using — it ends once.</summary>
        private sealed class Listening(IAsyncDisposable handler, CancellationTokenSource listening, Task held, Func<Task> cancelOnServer) : IAsyncDisposable
        {
            private bool _ended;

            public async ValueTask DisposeAsync()
            {
                if (_ended)
                    return;

                _ended = true;
                await cancelOnServer();
                await listening.CancelAsync();
                await handler.DisposeAsync();

                try
                {
                    await held;
                }
                catch (OperationCanceledException)
                {
                }

                listening.Dispose();
            }
        }
    }
}

/// <summary>What the sample jobs answer with.</summary>
public sealed record Outcome(string Text);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Outcome))]
internal sealed partial class SampleJson : JsonSerializerContext;

using System.ComponentModel;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.JobClient;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>A client waits for a job by being told: over stdio and over HTTP, on either protocol revision, with a lost notification covered.</summary>
public sealed class JobClientTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task CallJob_OverStdio_AnswersWithTheResultWhenTheJobEnds(string protocolVersion)
    {
        await using var stand = await StreamStand.StartAsync(protocolVersion);
        var jobs = stand.Host.Services.GetRequiredService<Jobs>();
        var seen = new List<JobState>();

        var report = await stand.Client.CallJobAsync("sample_slow", new Dictionary<string, object?> { ["text"] = "told" },
            new JobAwaitOptions { Progress = new Progress<JobReport>(each => seen.Add(each.State)) }).WaitAsync(Patience);

        Assert.Equal(JobState.Succeeded, report.State);
        Assert.Equal("told", report.Result!.Value.GetProperty("text").GetString());
        Assert.Contains(JobState.Running, seen);
        Assert.Empty(jobs.InFlight);
    }

    [Fact]
    public async Task CallJob_WhoseToolAnswersAtOnce_ReturnsTheReportAsItIs()
    {
        await using var stand = await StreamStand.StartAsync("2025-11-25");

        var report = await stand.Client.CallJobAsync("sample_quick", new Dictionary<string, object?> { ["text"] = "now" }).WaitAsync(Patience);

        Assert.Equal(JobState.Succeeded, report.State);
        Assert.Equal("now", report.Result!.Value.GetProperty("text").GetString());
    }

    /// <summary>A notification the transport lost costs a wait of the fallback, never a hang: the resource is read again on its own.</summary>
    [Fact]
    public async Task AwaitJob_WithNoNotificationArriving_ReadsTheResourceAfterTheFallback()
    {
        await using var stand = await StreamStand.StartAsync("2025-11-25");
        var jobs = stand.Host.Services.GetRequiredService<Jobs>();
        var release = new TaskCompletionSource();
        var job = jobs.Start("silent", async (_, token) =>
        {
            await release.Task.WaitAsync(token);

            return new Outcome("read anyway");
        }, SampleJson.Default.Outcome);
        var options = new JobAwaitOptions { Fallback = TimeSpan.FromMilliseconds(200) };

        stand.Host.Services.GetRequiredService<JobWatchers>().IsMute = true;
        var awaiting = stand.Client.AwaitJobAsync(job.Uri, options);
        release.SetResult();
        var report = await awaiting.WaitAsync(Patience);

        Assert.Equal(JobState.Succeeded, report.State);
    }

    [Fact]
    public async Task AwaitJob_Cancelled_EndsTheWaitAndNotTheJob()
    {
        await using var stand = await StreamStand.StartAsync("2025-11-25");
        var jobs = stand.Host.Services.GetRequiredService<Jobs>();
        var job = jobs.Start("long", async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);

            return new Outcome("never");
        }, SampleJson.Default.Outcome);
        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stand.Client.AwaitJobAsync(job.Uri, cancellationToken: giveUp.Token));

        Assert.True(job.Report.IsRunning);
        job.Cancel();
    }

    [Fact]
    public async Task ReadJob_OfAResourceThatIsNoJob_IsRefused()
    {
        await using var stand = await StreamStand.StartAsync("2025-11-25");

        await Assert.ThrowsAnyAsync<Exception>(() => stand.Client.ReadJobAsync("job://sample/none/9"));
    }

    /// <summary>The same wait over the HTTP host a shared server runs in, on the revision the SDK's client prefers.</summary>
    [Fact]
    public async Task CallJob_OverHttp_AnswersWithTheResultWhenTheJobEnds()
    {
        var port = FreePort();
        var builder = McpHttpHost.CreateBuilder([], new Uri($"http://127.0.0.1:{port}"));

        builder.Services.AddMcpServer().WithTools<SampleJobTools>(ToolJson.Options(SampleJson.Default)).WithLocalHttpTransport().WithJobs("sample");

        var app = builder.Build();
        app.MapLocalMcp();
        await app.StartAsync();

        try
        {
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"http://127.0.0.1:{port}{McpHttpHost.DefaultPath}") }));

            var report = await client.CallJobAsync("sample_slow", new Dictionary<string, object?> { ["text"] = "over http" }).WaitAsync(Patience);

            Assert.Equal(JobState.Succeeded, report.State);
            Assert.Equal("over http", report.Result!.Value.GetProperty("text").GetString());
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static int FreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    /// <summary>The sample server over a pair of pipes, with its job tools, and the client connected to it.</summary>
    private sealed class StreamStand : IAsyncDisposable
    {
        private StreamStand(IHost host, McpClient client)
        {
            Host = host;
            Client = client;
        }

        public IHost Host { get; }

        public McpClient Client { get; }

        public static async Task<StreamStand> StartAsync(string protocolVersion)
        {
            var toServer = new Pipe();
            var toClient = new Pipe();
            var builder = McpHost.CreateBuilder([]);

            builder.Services.AddMcpServer()
                .WithTools<SampleJobTools>(ToolJson.Options(SampleJson.Default))
                .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream())
                .WithJobs("sample");

            var host = builder.Build();
            await host.StartAsync();
            var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), new McpClientOptions { ProtocolVersion = protocolVersion });

            return new StreamStand(host, client);
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Host.StopAsync();
            Host.Dispose();
        }
    }
}

/// <summary>Two job tools: one whose work takes a moment, one whose work is done before the wait.</summary>
[McpServerToolType]
public sealed class SampleJobTools(Jobs jobs)
{
    [McpServerTool(Name = "sample_slow"), Description("Echoes the text after a moment, as a job.")]
    public async Task<JobReport> SlowAsync([Description("What to echo.")] string text, [Description("Seconds to wait for the result.")] int wait = 60, CancellationToken cancellationToken = default)
    {
        var job = jobs.Start("slow", async (progress, token) =>
        {
            progress.Report(new JobProgress(0.5, What: "halfway"));
            await Task.Delay(150, token);

            return new Outcome(text);
        }, SampleJson.Default.Outcome);

        return await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);
    }

    [McpServerTool(Name = "sample_quick"), Description("Echoes the text at once, as a job already ended.")]
    public async Task<JobReport> QuickAsync([Description("What to echo.")] string text, [Description("Seconds to wait for the result.")] int wait = 60, CancellationToken cancellationToken = default)
    {
        var job = jobs.Start("quick", (_, _) => Task.FromResult(new Outcome(text)), SampleJson.Default.Outcome);

        await job.Completion.WaitAsync(cancellationToken);

        return await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);
    }
}

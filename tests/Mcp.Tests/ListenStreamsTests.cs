using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Discovery;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>
/// The listen stream a sessionless client of the 2026-07-28 revision holds open over HTTP: what it asked for and the server
/// declares is acknowledged, and a skill's tools arriving reach it beside the updates of its jobs.
/// </summary>
/// <remarks>
/// WithJobs put a listen handler of its own in place of the MCP SDK's that granted resources alone, so a skill loaded by such
/// a client of Snail.MCP.AfterEffects or Blender was never told to it.
/// </remarks>
public sealed class ListenStreamsTests
{
    private const string Sessionless = "2026-07-28";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SkillLoaded_BySessionlessClient_ReachesItsListenStream_BesideItsJob()
    {
        await using var server = await StartAsync(jobs: true);
        await using var client = await server.SessionAsync(options: new McpClientOptions { ProtocolVersion = Sessionless });
        var release = new TaskCompletionSource();
        var job = server.Services.GetRequiredService<Jobs>().Start("slow", async (_, token) =>
        {
            await release.Task.WaitAsync(token);

            return new Outcome("ended");
        }, SampleJson.Default.Outcome);
        await using var listening = await Listening.OpenAsync(client, new JsonObject { ["toolsListChanged"] = true, ["promptsListChanged"] = true, ["resourceSubscriptions"] = new JsonArray(job.Uri) });

        var enabled = await client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        await listening.ToolsChanged.WaitAsync(Patience);
        var listed = (await client.ListToolsAsync()).Select(tool => tool.Name);
        release.SetResult();
        var updated = await listening.Updated.WaitAsync(Patience);

        Assert.True(string.IsNullOrEmpty(client.SessionId));
        Assert.True(enabled.IsOk(), enabled.Text());
        Assert.True(listening.Granted["toolsListChanged"]!.GetValue<bool>());
        Assert.Null(listening.Granted["promptsListChanged"]);
        Assert.Equal([job.Uri], listening.Granted["resourceSubscriptions"]!.AsArray().Select(uri => uri!.GetValue<string>()));
        Assert.Contains("render_image", listed);
        Assert.Equal(job.Uri, updated);
        Assert.Equal(listening.Id, listening.SubscriptionIdOf(NotificationMethods.ToolListChangedNotification)?.GetValue<string>());
        Assert.Equal(listening.Id, listening.SubscriptionIdOf(NotificationMethods.ResourceUpdatedNotification)?.GetValue<string>());
    }

    /// <summary>Skills without jobs over HTTP: the MCP SDK's own stateless listen handler grants nothing, so the one skills put in place carries the change.</summary>
    [Fact]
    public async Task SkillLoaded_OnAServerWithoutJobs_ReachesTheListenStream()
    {
        await using var server = await StartAsync(jobs: false, mode: HttpServerSessionMode.Stateless);
        await using var client = await server.SessionAsync(options: new McpClientOptions { ProtocolVersion = Sessionless });
        await using var listening = await Listening.OpenAsync(client, new JsonObject { ["toolsListChanged"] = true });

        await client.CallToolAsync("render_image");
        await listening.ToolsChanged.WaitAsync(Patience);

        Assert.True(listening.Granted["toolsListChanged"]!.GetValue<bool>());
    }

    /// <summary>
    /// An expiry happens outside any request; over HTTP each request's options are built anew, so it is heard only because
    /// every options instance shares one tool collection.
    /// </summary>
    [Fact]
    public async Task SkillExpired_ReachesTheListenStream_OutsideAnyRequest()
    {
        var clock = new ManualClock();
        await using var server = await StartAsync(jobs: true, clock: clock, idle: TimeSpan.FromMinutes(10));
        await using var client = await server.SessionAsync(options: new McpClientOptions { ProtocolVersion = Sessionless });
        await client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        await using var listening = await Listening.OpenAsync(client, new JsonObject { ["toolsListChanged"] = true });

        clock.Advance(TimeSpan.FromMinutes(11));
        server.Services.GetServices<IHostedService>().OfType<SkillExpiry>().Single().Sweep();
        await listening.ToolsChanged.WaitAsync(Patience);
        var listed = (await client.ListToolsAsync()).Select(tool => tool.Name);

        Assert.DoesNotContain("render_image", listed);
    }

    /// <summary>Two sessionless clients told apart by the server (AfterEffects: by the name of their key) keep a list each.</summary>
    [Fact]
    public async Task SessionlessClients_ToldApartBySessionOf_KeepAListEach()
    {
        await using var server = await StartAsync(jobs: true, skilled: options => options.SessionOf = (services, _) =>
            services.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Query["client"].FirstOrDefault());
        await using var titles = await server.SessionAsync("client=titles", new McpClientOptions { ProtocolVersion = Sessionless });
        await using var edit = await server.SessionAsync("client=edit", new McpClientOptions { ProtocolVersion = Sessionless });

        await titles.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        var titled = (await titles.ListToolsAsync()).Select(tool => tool.Name);
        var edited = (await edit.ListToolsAsync()).Select(tool => tool.Name);

        Assert.Contains("render_image", titled);
        Assert.DoesNotContain("render_image", edited);
    }

    /// <summary>A stream that asks for nothing a server declares is acknowledged with nothing, and told nothing when a skill loads.</summary>
    [Fact]
    public async Task Stream_ThatDidNotAskForTools_IsNotToldOfThem()
    {
        await using var server = await StartAsync(jobs: true);
        await using var client = await server.SessionAsync(options: new McpClientOptions { ProtocolVersion = Sessionless });
        await using var listening = await Listening.OpenAsync(client, new JsonObject { ["resourcesListChanged"] = true, ["resourceSubscriptions"] = new JsonArray("job://sample/slow/404") });

        await client.CallToolAsync("sample_enable_skill", new Dictionary<string, object?> { ["name"] = "render" });
        var told = await Task.WhenAny(listening.ToolsChanged, Task.Delay(500));

        Assert.Empty(listening.Granted.AsObject());
        Assert.NotSame(listening.ToolsChanged, told);
    }

    /// <summary>A server of the sample tools over HTTP, serving a client of the sessionless revision as AfterEffects and Blender do.</summary>
    private static Task<InProcessServer> StartAsync(
        bool jobs,
        ManualClock? clock = null,
        TimeSpan idle = default,
        HttpServerSessionMode mode = HttpServerSessionMode.StatefulForInitializeClients,
        Action<SkillOptions>? skilled = null) =>
        InProcessServer.StartAsync(builder =>
        {
            if (clock is not null)
                builder.Services.AddSingleton<TimeProvider>(clock);

            builder.Services.AddHttpContextAccessor();

            var server = builder.Services.AddMcpServer()
                .WithHttpTransport(options => options.SessionMode = mode)
                .WithTools<FileSample>()
                .WithTools<RenderSample>();

            if (jobs)
                server.WithJobs("sample");

            server.WithSkills(options =>
            {
                (options.Prefix, options.IdleTimeout) = ("sample", idle);
                skilled?.Invoke(options);
            });
        });

    /// <summary>A listen request held open, with what it was granted and what reached it; ended when disposed.</summary>
    private sealed class Listening : IAsyncDisposable
    {
        private readonly List<IAsyncDisposable> _handlers = [];
        private readonly Dictionary<string, JsonNode?> _ids = new(StringComparer.Ordinal);
        private readonly TaskCompletionSource _toolsChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _updated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _listening = new();
        private Task _held = Task.CompletedTask;

        public JsonNode Granted { get; private set; } = new JsonObject();

        /// <summary>The id the request was opened under, which every notification on its stream carries.</summary>
        public string Id { get; } = $"listen-{Guid.NewGuid():N}";

        public Task ToolsChanged => _toolsChanged.Task;

        public Task<string> Updated => _updated.Task;

        public JsonNode? SubscriptionIdOf(string method)
        {
            lock (_ids)
                return _ids.GetValueOrDefault(method);
        }

        public static async Task<Listening> OpenAsync(McpClient client, JsonObject notifications)
        {
            var listening = new Listening();
            var acknowledged = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            var request = new JsonRpcRequest { Id = new RequestId(listening.Id), Method = RequestMethods.SubscriptionsListen, Params = new JsonObject { ["notifications"] = notifications } };

            listening._handlers.Add(client.RegisterNotificationHandler(NotificationMethods.SubscriptionsAcknowledgedNotification, (notification, _) =>
            {
                acknowledged.TrySetResult(notification.Params?["notifications"]?.DeepClone() ?? new JsonObject());

                return ValueTask.CompletedTask;
            }));
            listening._handlers.Add(client.RegisterNotificationHandler(NotificationMethods.ToolListChangedNotification, (notification, _) =>
            {
                listening.Heard(notification);
                listening._toolsChanged.TrySetResult();

                return ValueTask.CompletedTask;
            }));
            listening._handlers.Add(client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification, (notification, _) =>
            {
                listening.Heard(notification);

                if (notification.Params?["uri"]?.GetValue<string>() is { } uri)
                    listening._updated.TrySetResult(uri);

                return ValueTask.CompletedTask;
            }));
            listening._held = client.SendRequestAsync(request, listening._listening.Token);
            listening.Granted = await acknowledged.Task.WaitAsync(Patience);

            return listening;
        }

        public async ValueTask DisposeAsync()
        {
            await _listening.CancelAsync();

            foreach (var handler in _handlers)
                await handler.DisposeAsync();

            try
            {
                await _held;
            }
            catch (Exception ended) when (ended is OperationCanceledException or McpException or IOException or HttpRequestException)
            {
            }

            _listening.Dispose();
        }

        private void Heard(JsonRpcNotification notification)
        {
            lock (_ids)
                _ids[notification.Method] = notification.Params?["_meta"]?[MetaKeys.SubscriptionId]?.DeepClone();
        }
    }
}

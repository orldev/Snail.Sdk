using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>Puts a server's jobs on the protocol: a resource per job, subscriptions to it, and the listen stream of a sessionless client.</summary>
public static class JobsRegistration
{
    extension(IMcpServerBuilder builder)
    {
        /// <summary>
        /// The jobs of the server named <paramref name="server"/>: <see cref="Jobs"/> for its tools to start work on, each
        /// job readable at <c>job://&lt;server&gt;/&lt;kind&gt;/&lt;id&gt;</c>, every change sent to the sessions
        /// subscribed to it, and the server's running jobs cancelled when the host stops.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A session subscribes with <c>resources/subscribe</c> and is sent <c>notifications/resources/updated</c> on each
        /// change of the job — its state, or a progress step, those no more than once a second. A client of the
        /// 2026-07-28 revision, served per request over HTTP, opens <c>subscriptions/listen</c> instead: the stream is
        /// acknowledged and held open until the client closes it, since a client that reads its end as the end of the
        /// connection drops every tool (Claude Code did, in 172 of 652 sessions of Snail.MCP.Blender), and the updates of
        /// the resources it asked for are sent on it.
        /// </para>
        /// <para>Called twice, it registers once.</para>
        /// </remarks>
        public IMcpServerBuilder WithJobs(string server)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(server);

            if (builder.Services.Any(service => service.ServiceType == typeof(Jobs)))
                return builder;

            builder.Services.AddSingleton(provider => new Jobs(server, provider.GetService<TimeProvider>(), provider.GetService<IHostApplicationLifetime>()));
            builder.Services.AddSingleton<JobWatchers>();
            builder.Services.AddOptions<McpServerOptions>().PostConfigure<JobWatchers>(JobWatchers.Configure);

            return builder;
        }
    }
}

/// <summary>The sessions watching jobs, and the notifications that reach them.</summary>
/// <remarks>
/// The MCP SDK keeps no subscriptions of its own on the server: the subscribe and unsubscribe handlers are the
/// server's to write, and so is sending. One instance serves every session of the host, keyed by the server of the
/// session, which a stdio host has one of and an HTTP host one per client.
/// </remarks>
internal sealed class JobWatchers(Jobs jobs, TimeProvider? clock = null)
{
    /// <summary>How often at most a progress step reaches a watcher; a change of state reaches it at once.</summary>
    internal static readonly TimeSpan Throttle = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private readonly Dictionary<object, Watching> _sessions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, long> _sentAt = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private bool _listening;

    /// <summary>Wires the handlers and the capability into the server's options, and the jobs' changes into sending.</summary>
    public static void Configure(McpServerOptions options, JobWatchers watchers)
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Resources ??= new ResourcesCapability();
        options.Capabilities.Resources.Subscribe = true;
        options.Handlers.SubscribeToResourcesHandler = watchers.SubscribeAsync;
        options.Handlers.UnsubscribeFromResourcesHandler = watchers.UnsubscribeAsync;
        options.Handlers.SubscriptionsListenHandler = watchers.ListenAsync;
        options.Handlers.ReadResourceHandler = watchers.Wrap(options.Handlers.ReadResourceHandler);
        options.Handlers.ListResourceTemplatesHandler = watchers.Templates(options.Handlers.ListResourceTemplatesHandler);
        watchers.Listen();
    }

    /// <summary>Drops every notification instead of sending it, for a test of a client whose notifications were lost.</summary>
    internal bool IsMute { get; set; }

    /// <summary>The sessions watching a URI, for a test to see.</summary>
    internal int WatchersOf(string uri)
    {
        lock (_gate)
            return _sessions.Values.Count(watching => watching.Uris.Contains(uri));
    }

    /// <summary>
    /// What one session is known by across its requests. The server a handler is given is bound to the request, a new
    /// one each time; the session's service provider is the session's own, the same object for every request of it.
    /// </summary>
    private static object KeyOf(McpServer server) => (object?)server.Services ?? server;

    private void Listen()
    {
        lock (_gate)
        {
            if (_listening)
                return;

            _listening = true;
        }

        jobs.Changed += report => _ = SendAsync(report);
    }

    private ValueTask<EmptyResult> SubscribeAsync(RequestContext<SubscribeRequestParams> request, CancellationToken cancellationToken)
    {
        if (request.Params?.Uri is { } uri)
        {
            lock (_gate)
            {
                var key = KeyOf(request.Server);

                if (!_sessions.TryGetValue(key, out var watching))
                    _sessions[key] = watching = new Watching(request.Server);

                watching.Uris.Add(uri);
            }
        }

        return ValueTask.FromResult(new EmptyResult());
    }

    private ValueTask<EmptyResult> UnsubscribeAsync(RequestContext<UnsubscribeRequestParams> request, CancellationToken cancellationToken)
    {
        if (request.Params?.Uri is { } uri)
        {
            lock (_gate)
            {
                var key = KeyOf(request.Server);

                if (_sessions.TryGetValue(key, out var watching) && watching.Uris.Remove(uri) && watching.Uris.Count == 0)
                    _sessions.Remove(key);
            }
        }

        return ValueTask.FromResult(new EmptyResult());
    }

    /// <summary>Acknowledges the resources the client asked for and holds the stream open until the client closes it or the host stops.</summary>
    private async ValueTask<EmptyResult> ListenAsync(RequestContext<SubscriptionsListenRequestParams> request, CancellationToken cancellationToken)
    {
        var asked = request.Params?.Notifications?.ResourceSubscriptions ?? [];
        var granted = new SubscriptionsListenNotifications { ResourceSubscriptions = asked.Count > 0 ? [.. asked] : null };
        var acknowledgement = JsonSerializer.SerializeToNode(new SubscriptionsAcknowledgedNotificationParams { Notifications = granted }, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(SubscriptionsAcknowledgedNotificationParams)))!.AsObject();

        acknowledgement["_meta"] = new JsonObject { [MetaKeys.SubscriptionId] = JsonSerializer.SerializeToNode(request.JsonRpcRequest.Id, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(RequestId))) };

        await request.Server.SendMessageAsync(new JsonRpcNotification { Method = NotificationMethods.SubscriptionsAcknowledgedNotification, Params = acknowledgement }, cancellationToken).ConfigureAwait(false);

        var key = KeyOf(request.Server);

        lock (_gate)
        {
            if (asked.Count > 0)
                _sessions[key] = new Watching(request.Server, asked);
        }

        try
        {
            await HeldOpenAsync(request.Services?.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
                _sessions.Remove(key);
        }

        return new EmptyResult();
    }

    private static async Task HeldOpenAsync(CancellationToken stopping, CancellationToken closed)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(stopping, closed);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = either.Token.Register(() => ended.TrySetResult());

        await ended.Task.ConfigureAwait(false);
    }

    /// <summary>A job's report at its URI; any other URI goes to the handler the server had, or is unknown.</summary>
    private McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> Wrap(McpRequestHandler<ReadResourceRequestParams, ReadResourceResult>? inner) =>
        (request, cancellationToken) =>
        {
            if (request.Params?.Uri is { } uri && jobs.Find(uri) is { } job)
            {
                var contents = new TextResourceContents { Uri = uri, MimeType = "application/json", Text = job.Report.ToJson() };

                return ValueTask.FromResult(new ReadResourceResult { Contents = [contents] });
            }

            if (inner is not null)
                return inner(request, cancellationToken);

            throw new McpException($"Unknown resource: '{request.Params?.Uri}'");
        };

    /// <summary>The job template listed with whatever templates the server had.</summary>
    private McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult> Templates(McpRequestHandler<ListResourceTemplatesRequestParams, ListResourceTemplatesResult>? inner) =>
        async (request, cancellationToken) =>
        {
            var listed = inner is not null ? await inner(request, cancellationToken).ConfigureAwait(false) : new ListResourceTemplatesResult { ResourceTemplates = [] };
            var template = new ResourceTemplate { UriTemplate = jobs.UriTemplate, Name = "job", Title = "A job of this server", MimeType = "application/json", Description = "Where a job stands: its state, progress, message and, once it succeeded, its result." };

            listed.ResourceTemplates = [.. listed.ResourceTemplates ?? [], template];

            return listed;
        };

    private async Task SendAsync(JobReport report)
    {
        List<KeyValuePair<object, Watching>> watching;

        lock (_gate)
        {
            if (IsMute || (report.IsRunning && _sentAt.TryGetValue(report.Uri, out var last) && _clock.GetElapsedTime(last) < Throttle))
                return;

            _sentAt[report.Uri] = _clock.GetTimestamp();
            watching = [.. _sessions.Where(session => session.Value.Uris.Contains(report.Uri))];
        }

        foreach (var (key, session) in watching)
        {
            try
            {
                await session.Server.SendNotificationAsync(NotificationMethods.ResourceUpdatedNotification, new ResourceUpdatedNotificationParams { Uri = report.Uri }, McpJsonUtilities.DefaultOptions, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception gone) when (gone is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
            {
                lock (_gate)
                    _sessions.Remove(key);
            }
        }
    }

    /// <summary>One session's watch: the server to send on and the URIs it asked for.</summary>
    private sealed class Watching(McpServer server, IEnumerable<string>? uris = null)
    {
        public McpServer Server { get; } = server;

        public HashSet<string> Uris { get; } = new(uris ?? [], StringComparer.Ordinal);
    }
}

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>
/// The <c>subscriptions/listen</c> streams the clients of the 2026-07-28 revision hold open, and every notification that
/// reaches them: the changes of the tool, prompt and resource lists, and the updates of the resources they asked for.
/// </summary>
/// <remarks>
/// <para>
/// A handler of <c>subscriptions/listen</c> replaces the MCP SDK's whole: the SDK then acknowledges nothing and carries
/// no <c>*/list_changed</c> of its own, and its stateless handler grants nothing at all. Jobs needed a handler of their
/// own for the resources it carries, and the first one granted those alone — so a client of the revision that loaded a
/// skill on Snail.MCP.AfterEffects or Blender never heard its tools arrive. One handler serves every kind here, and
/// <c>WithJobs</c> and <c>WithSkills</c> both put it in place.
/// </para>
/// <para>
/// A kind is granted when the client asked for it and the server declares it: a list that changes when its collection
/// is there or its capability says so, as the MCP SDK declares it; a resource when the server knows how to watch it
/// (<see cref="Watches"/>, the jobs'). Every notification carries the stream's id under
/// <c>_meta/io.modelcontextprotocol/subscriptionId</c>, so a client sharing one channel among streams tells them apart.
/// </para>
/// <para>
/// Over HTTP each request of a sessionless client is served with options built anew, collections included, so a change
/// is not heard from one collection only: the collection of every stream is listened to, and a change of any of them
/// reaches every stream granted its kind. A skill's tool list, which no collection change carries outside the skill's
/// own collection, is announced through it (<c>SkillToolCollection</c>), which every options instance of the host shares.
/// </para>
/// </remarks>
internal sealed class ListenStreams
{
    private readonly Lock _gate = new();

    private readonly List<Stream> _streams = [];

    private readonly ConditionalWeakTable<object, object> _heard = [];

    /// <summary>Puts the one listen handler on the server's options; called by every registration that needs it, it registers once.</summary>
    public static void Register(IServiceCollection services)
    {
        if (services.Any(service => service.ServiceType == typeof(ListenStreams)))
            return;

        services.AddSingleton<ListenStreams>();
        services.AddOptions<McpServerOptions>().PostConfigure<ListenStreams>((options, streams) => options.Handlers.SubscriptionsListenHandler = streams.ListenAsync);
    }

    /// <summary>Whether a request may watch a resource on its stream; no resource is granted while unset.</summary>
    public Func<string, IServiceProvider?, bool>? Watches { get; set; }

    /// <summary>The streams watching a URI, for a test to see.</summary>
    public int WatchersOf(string uri)
    {
        lock (_gate)
            return _streams.Count(stream => stream.Uris.Contains(uri));
    }

    /// <summary>Tells every stream that asked for the resource that it changed.</summary>
    public Task UpdatedAsync(string uri) =>
        SendAsync(stream => stream.Uris.Contains(uri), NotificationMethods.ResourceUpdatedNotification, () => new JsonObject { ["uri"] = uri });

    /// <summary>Tells every stream granted the list of <paramref name="method"/> that it changed.</summary>
    public Task ChangedAsync(string method) => SendAsync(stream => stream.Grants(method), method, () => []);

    /// <summary>Acknowledges what the client asked for and the server declares, and holds the stream open until the client closes it or the host stops.</summary>
    /// <remarks>
    /// The stream is held before it is acknowledged, and what reaches it meanwhile waits for the acknowledgement: added
    /// after it, a job's update or a skill's list change that came in between was lost, and a client heard of a skill's
    /// tools only on its next change. The acknowledgement still goes first, as the revision requires.
    /// </remarks>
    private async ValueTask<EmptyResult> ListenAsync(RequestContext<SubscriptionsListenRequestParams> request, CancellationToken cancellationToken)
    {
        var options = request.Server.ServerOptions;
        var asked = request.Params?.Notifications ?? new SubscriptionsListenNotifications();
        var granted = Granted(asked, options, request.Services);
        var stream = new Stream(request.Server, request.JsonRpcRequest.Id, granted);
        var acknowledgement = JsonSerializer.SerializeToNode(new SubscriptionsAcknowledgedNotificationParams { Notifications = granted }, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(SubscriptionsAcknowledgedNotificationParams)))!.AsObject();

        Hear(options);

        lock (_gate)
            _streams.Add(stream);

        try
        {
            await stream.AcknowledgeAsync(acknowledgement, cancellationToken).ConfigureAwait(false);
            await HeldOpenAsync(request.Services?.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? CancellationToken.None, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
                _streams.Remove(stream);
        }

        return new EmptyResult();
    }

    /// <summary>What of the asked the server delivers: a list it declares changing, a resource it can watch for the request.</summary>
    private SubscriptionsListenNotifications Granted(SubscriptionsListenNotifications asked, McpServerOptions options, IServiceProvider? services)
    {
        var capabilities = options.Capabilities;
        IReadOnlyList<string> uris = Watches is { } watches ? [.. (asked.ResourceSubscriptions ?? []).Where(uri => watches(uri, services))] : [];

        return new SubscriptionsListenNotifications
        {
            ToolsListChanged = Only(asked.ToolsListChanged == true && (options.ToolCollection is not null || capabilities?.Tools?.ListChanged == true)),
            PromptsListChanged = Only(asked.PromptsListChanged == true && (options.PromptCollection is not null || capabilities?.Prompts?.ListChanged == true)),
            ResourcesListChanged = Only(asked.ResourcesListChanged == true && (options.ResourceCollection is not null || capabilities?.Resources?.ListChanged == true)),
            ResourceSubscriptions = uris.Count > 0 ? [.. uris] : null,
        };
    }

    /// <summary>True when granted, and absent rather than false otherwise, as the MCP SDK writes an acknowledgement.</summary>
    private static bool? Only(bool isGranted) => isGranted ? true : null;

    /// <summary>Listens to the collections of these options, once each; a collection no options hold any more is let go.</summary>
    private void Hear(McpServerOptions options)
    {
        Hear(options.ToolCollection, NotificationMethods.ToolListChangedNotification);
        Hear(options.PromptCollection, NotificationMethods.PromptListChangedNotification);
        Hear(options.ResourceCollection, NotificationMethods.ResourceListChangedNotification);
    }

    private void Hear<TPrimitive>(McpServerPrimitiveCollection<TPrimitive>? collection, string method)
        where TPrimitive : IMcpServerPrimitive
    {
        if (collection is null)
            return;

        lock (_gate)
        {
            if (_heard.TryGetValue(collection, out _))
                return;

            _heard.Add(collection, method);
        }

        collection.Changed += (_, _) => _ = ChangedAsync(method);
    }

    private async Task SendAsync(Func<Stream, bool> isFor, string method, Func<JsonObject> parameters)
    {
        List<Stream> reached;

        lock (_gate)
            reached = [.. _streams.Where(isFor)];

        foreach (var stream in reached)
        {
            try
            {
                await stream.NotifyAsync(method, parameters(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception gone) when (gone is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
            {
                lock (_gate)
                    _streams.Remove(stream);
            }
        }
    }

    private static async Task HeldOpenAsync(CancellationToken stopping, CancellationToken closed)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(stopping, closed);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = either.Token.Register(() => ended.TrySetResult());

        await ended.Task.ConfigureAwait(false);
    }

    /// <summary>One held stream: the server of its request, which sends on the request's own response, its id, and what it was granted.</summary>
    private sealed class Stream(McpServer server, RequestId id, SubscriptionsListenNotifications granted)
    {
        private readonly TaskCompletionSource _acknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HashSet<string> Uris { get; } = new(granted.ResourceSubscriptions ?? [], StringComparer.Ordinal);

        public bool Grants(string method) => method switch
        {
            NotificationMethods.ToolListChangedNotification => granted.ToolsListChanged == true,
            NotificationMethods.PromptListChangedNotification => granted.PromptsListChanged == true,
            NotificationMethods.ResourceListChangedNotification => granted.ResourcesListChanged == true,
            _ => false,
        };

        /// <summary>Sends the acknowledgement, and lets the notifications that waited for it go.</summary>
        public async Task AcknowledgeAsync(JsonObject acknowledgement, CancellationToken cancellationToken)
        {
            try
            {
                await SendAsync(NotificationMethods.SubscriptionsAcknowledgedNotification, acknowledgement, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _acknowledged.TrySetResult();
            }
        }

        /// <summary>Sends a notification once the stream is acknowledged.</summary>
        public async Task NotifyAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
        {
            await _acknowledged.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await SendAsync(method, parameters, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Sends a notification on the stream, tagged with the stream's id.</summary>
        private Task SendAsync(string method, JsonObject parameters, CancellationToken cancellationToken)
        {
            parameters["_meta"] = new JsonObject { [MetaKeys.SubscriptionId] = JsonSerializer.SerializeToNode(id, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(RequestId))) };

            return server.SendMessageAsync(new JsonRpcNotification { Method = method, Params = parameters }, cancellationToken);
        }
    }
}

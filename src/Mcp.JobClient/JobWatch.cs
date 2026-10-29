using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.JobClient;

/// <summary>A watch on one resource, opened the way the negotiated protocol revision allows, ended by disposing it.</summary>
/// <remarks>
/// <para>
/// Up to the 2025-11-25 revision a session subscribes with <c>resources/subscribe</c> and is sent
/// <c>notifications/resources/updated</c>. From 2026-07-28 that method is gone: the client opens
/// <c>subscriptions/listen</c>, a request the server holds open and streams the notifications on, tagged with the
/// request's id. The MCP SDK's client sends the first and knows nothing of the second, so the second is sent here.
/// </para>
/// <para>
/// The listen request carries an id of the client's own: the SDK numbers a request inside its send without writing
/// the number back, so a cancellation named after the request's id would name none, and cancelling the token alone
/// tells the server nothing. What ends the stream on the server is <c>notifications/cancelled</c> with that id.
/// </para>
/// </remarks>
internal sealed class JobWatch : IAsyncDisposable
{
    private const string ListenRevision = "2026-07-28";

    private readonly IAsyncDisposable _handler;
    private readonly McpClient _client;
    private readonly RequestId? _listening;
    private readonly Task? _held;
    private bool _ended;

    private JobWatch(McpClient client, IAsyncDisposable handler, RequestId? listening, Task? held)
    {
        _client = client;
        _handler = handler;
        _listening = listening;
        _held = held;
    }

    /// <summary>Opens the watch and calls <paramref name="updated"/> with every notification of the resource's change.</summary>
    public static async Task<JobWatch> OpenAsync(McpClient client, string uri, Action updated, CancellationToken cancellationToken)
    {
        if (!IsListening(client))
        {
            var subscription = await client.SubscribeToResourceAsync(uri, (_, _) =>
            {
                updated();

                return ValueTask.CompletedTask;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            return new JobWatch(client, subscription, null, null);
        }

        var handler = client.RegisterNotificationHandler(NotificationMethods.ResourceUpdatedNotification, (notification, _) =>
        {
            if (string.Equals(notification.Params?["uri"]?.GetValue<string>(), uri, StringComparison.Ordinal))
                updated();

            return ValueTask.CompletedTask;
        });
        var id = new RequestId($"job-watch-{Guid.NewGuid():N}");
        var request = new JsonRpcRequest
        {
            Id = id,
            Method = RequestMethods.SubscriptionsListen,
            Params = new JsonObject { ["notifications"] = new JsonObject { ["resourceSubscriptions"] = new JsonArray(uri) } },
        };
        var held = client.SendRequestAsync(request, CancellationToken.None);

        return new JobWatch(client, handler, id, held);
    }

    /// <summary>Whether the session speaks the revision that replaced resource subscriptions with the listen stream.</summary>
    public static bool IsListening(McpClient client) =>
        string.CompareOrdinal(client.NegotiatedProtocolVersion, ListenRevision) >= 0;

    /// <summary>Ends the watch: unsubscribes, or cancels the listen request on the server and waits for its stream to end.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_ended)
            return;

        _ended = true;

        if (_listening is { } id && _held is { } held)
        {
            await CancelAsync(id).ConfigureAwait(false);
            await EndedAsync(held).ConfigureAwait(false);
        }

        await _handler.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CancelAsync(RequestId id)
    {
        try
        {
            await _client.SendNotificationAsync(NotificationMethods.CancelledNotification, new CancelledNotificationParams { RequestId = id }, McpJsonUtilities.DefaultOptions, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception gone) when (gone is IOException or ObjectDisposedException or InvalidOperationException or McpException)
        {
        }
    }

    private static async Task EndedAsync(Task held)
    {
        try
        {
            await held.ConfigureAwait(false);
        }
        catch (Exception ended) when (ended is OperationCanceledException or McpException or IOException or ObjectDisposedException)
        {
        }
    }
}

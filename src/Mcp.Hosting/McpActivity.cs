using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>When the server last heard an MCP message, over any transport.</summary>
internal sealed class McpActivity(TimeProvider time)
{
    private long _heard = time.GetTimestamp();

    /// <summary>How long since the last message, or since the start when none came.</summary>
    public TimeSpan Quiet => time.GetElapsedTime(Interlocked.Read(ref _heard));

    /// <summary>Notes that a message came in.</summary>
    public void Heard() => Interlocked.Exchange(ref _heard, time.GetTimestamp());

    /// <summary>The incoming-message filter that notes every message before passing it on.</summary>
    public McpMessageHandler Hearing(McpMessageHandler next) =>
        (context, cancellationToken) =>
        {
            Heard();

            return next(context, cancellationToken);
        };
}

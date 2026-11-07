using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>Stops a stdio server that has heard no MCP message for a quiet period, unless the server says it is busy.</summary>
/// <remarks>
/// <para>
/// A client that dies does not always close the server's stdin — a grandchild holding the pipe, a killed terminal — and
/// the server it started then waits for a message that never comes, holding its memory and whatever it opened, until
/// the machine restarts. Four servers of the fleet carried a watcher of their own for it.
/// </para>
/// <para>
/// It never reads stdin: the MCP SDK's transport owns it, and a second reader would race it for the protocol's bytes.
/// While <see cref="IsBusy"/> says yes the server counts as heard, so the quiet period starts again when the work ends.
/// </para>
/// </remarks>
internal sealed partial class IdleShutdown(
    McpActivity activity,
    TimeSpan after,
    Func<bool> isBusy,
    bool isSingleSession,
    TimeProvider time,
    IHostApplicationLifetime lifetime,
    ILogger<IdleShutdown> logger) : BackgroundService
{
    private static readonly TimeSpan LongestTick = TimeSpan.FromMinutes(1);

    private long _busy = time.GetTimestamp();

    /// <summary>How long the server must hear nothing before it stops; zero when the shutdown is off.</summary>
    internal TimeSpan After => after;

    /// <summary>Whether the server says it is busy, which vetoes the stop.</summary>
    internal bool IsBusy => isBusy();

    /// <summary>Stops the host when the server has heard nothing for the period and is not busy.</summary>
    internal bool TryStop()
    {
        if (IsBusy)
        {
            _busy = time.GetTimestamp();

            return false;
        }

        var sinceBusy = time.GetElapsedTime(_busy);
        var quiet = activity.Quiet < sinceBusy ? activity.Quiet : sinceBusy;

        if (quiet < after)
            return false;

        Stopping(logger, quiet.TotalSeconds);
        lifetime.StopApplication();

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A server shared over HTTP is left running: it is the one server of the machine, kept by launchd or a container's
    /// restart policy, and stopping it between sessions would only make it start again cold.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (after <= TimeSpan.Zero || !isSingleSession)
            return;

        using var timer = new PeriodicTimer(after / 4 < LongestTick ? after / 4 : LongestTick, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (TryStop())
                    return;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
    }

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "No MCP message for {Seconds:F0} s: the client is gone, the server stops")]
    private static partial void Stopping(ILogger logger, double seconds);
}

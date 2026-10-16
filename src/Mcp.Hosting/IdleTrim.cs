using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>A compacting collection once the server has heard no MCP message for a quiet period.</summary>
/// <remarks>
/// <para>
/// A server works in bursts — a census, an index, a solution loaded — and whatever a burst kept alive long enough is
/// promoted to generation 2, where it dies. Nothing collects generation 2 again until the process allocates its budget,
/// and an idle server allocates nothing, so the garbage of its last burst stays committed for as long as the session
/// lasts. Measured on 2026-10-13: Snail.MCP.CleanUp held 412 MB committed after a census and 53 MB after the trim, a
/// 58 ms pause, and sat idle at 72 MB of footprint against 244 MB without it.
/// </para>
/// <para>
/// The trim runs once the server has been quiet for the period, only when the process has allocated
/// <see cref="WorthTrimming"/> since the last one, and at most once per period: a server that works in the background
/// while nobody calls it is paused once per period at most, and one that allocates nothing is never paused at all.
/// </para>
/// </remarks>
internal sealed partial class IdleTrim(
    McpActivity activity,
    TimeSpan period,
    TimeProvider time,
    Func<long> allocated,
    Func<IdleTrim.Compaction> compact,
    ILogger<IdleTrim> logger) : BackgroundService
{
    /// <summary>The variable that sets the quiet period in seconds; 0 turns the trim off.</summary>
    internal const string Variable = "SNAIL_MCP_IDLE_TRIM";

    /// <summary>How much the process must have allocated since the last trim for another to be worth its pause.</summary>
    /// <remarks>
    /// Idle, Snail.MCP.Roslyn allocated 0.1 MB in 30 s; its solution load, 300 MB. A forced trim of Plans and Vault, which
    /// never allocate this much, gave back nothing (12 and 14 MB of footprint before and after).
    /// </remarks>
    internal const long WorthTrimming = 32L * 1024 * 1024;

    /// <summary>The quiet period when the variable is not a number.</summary>
    internal static readonly TimeSpan DefaultPeriod = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan LongestTick = TimeSpan.FromMinutes(1);

    private long _trimmed = time.GetTimestamp();
    private long _allocatedAtTrim;

    /// <summary>How long the server must hear nothing before the heap is compacted; zero when the trim is off.</summary>
    internal TimeSpan Period => period;

    /// <summary>The quiet period the variable sets: its seconds, zero when it is 0 or negative, the default when it is not a number.</summary>
    internal static TimeSpan PeriodOf(string? variable)
    {
        if (!int.TryParse(variable, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return DefaultPeriod;

        return TimeSpan.FromSeconds(Math.Max(seconds, 0));
    }

    /// <summary>The real collection: every generation, compacted, the large object heap with it.</summary>
    internal static Compaction Compact()
    {
        var before = GC.GetGCMemoryInfo().TotalCommittedBytes;
        var paused = GC.GetTotalPauseDuration();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        return new Compaction(before, GC.GetGCMemoryInfo().TotalCommittedBytes, GC.GetTotalPauseDuration() - paused);
    }

    /// <summary>Trims when the server has been quiet for the period, has allocated enough since, and was not trimmed within it.</summary>
    internal bool TryTrim()
    {
        var isQuiet = activity.Quiet >= period;
        var isDue = time.GetElapsedTime(_trimmed) >= period;
        var isWorthItsPause = allocated() - _allocatedAtTrim >= WorthTrimming;

        if (!(isQuiet && isDue && isWorthItsPause))
            return false;

        var compaction = compact();

        _trimmed = time.GetTimestamp();
        _allocatedAtTrim = allocated();
        Trimmed(logger, compaction.Before, compaction.After, compaction.Pause.TotalMilliseconds);

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A host disposed without being stopped — a test's, a crashed client's — cancels the token before the lifetime says
    /// it is stopping, and the host would log the cancellation as a service that crashed; being told to stop is not a
    /// failure, so the loop ends quietly.
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (period <= TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(period / 4 < LongestTick ? period / 4 : LongestTick, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                TryTrim();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Quiet: heap compacted, {Before} bytes committed before, {After} after, paused {Pause:F0} ms")]
    private static partial void Trimmed(ILogger logger, long before, long after, double pause);

    /// <summary>What one compacting collection gave back and what it cost.</summary>
    internal readonly record struct Compaction(long Before, long After, TimeSpan Pause);
}

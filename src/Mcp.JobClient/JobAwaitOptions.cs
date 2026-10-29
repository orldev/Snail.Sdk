using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.Sdk.Mcp.JobClient;

/// <summary>How a job is awaited.</summary>
public sealed record JobAwaitOptions
{
    /// <summary>
    /// How long to go without a notification before the job's resource is read once anyway. A notification can be
    /// lost — a transport that dropped it, a session that moved — and a lost one costs a wait of this length, never a
    /// hang. Half a minute by default: one read a half-minute, not one every two seconds.
    /// </summary>
    public TimeSpan Fallback { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Told of every report that arrives while the job runs, when set.</summary>
    public IProgress<JobReport>? Progress { get; init; }
}

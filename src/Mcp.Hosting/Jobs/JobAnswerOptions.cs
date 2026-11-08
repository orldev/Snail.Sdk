namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>The words <see cref="JobAnswer"/> answers a job that failed or was cancelled with, where they are the server's own: the hint chosen for the report, and the error of a cancellation.</summary>
/// <remarks>
/// Kept once by a server and passed to every answer of its jobs —
/// <c>JobAnswer.Result(report, running, options: Words)</c> with <c>Words = new() { Hint = HintOf, Cancelled = "…" }</c> — where it
/// worked out a hint for each report before the call and could not word a cancellation at all.
/// </remarks>
public sealed record JobAnswerOptions
{
    /// <summary>What to do about a job that failed or was cancelled, chosen for its report — start it again, read its log; null, or null from it, answers with no hint.</summary>
    /// <remarks>Asked only for a job that failed or was cancelled, and only when neither its report nor the call carries a hint: the hint the work's answer gave wins, then the one the call passes.</remarks>
    public Func<JobReport, string?>? Hint { get; init; }

    /// <summary>The error of a cancelled job, in the server's words; null says "The job was cancelled before it ended."</summary>
    public string? Cancelled { get; init; }
}

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>The words <c>ExplainFailures</c> answers a failure nobody foresaw with: its error and its hint; the type stays <c>internal</c>.</summary>
public sealed class FailureOptions
{
    /// <summary>What to do about it; by default, that the server failed inside, to try once more, and to report it if it fails again.</summary>
    public string? Hint { get; set; } = ToolFailures.Hint;

    /// <summary>The error from the tool's name and what it threw; null answers with the message of what it threw.</summary>
    /// <remarks>A message may say more than a model should read — a path, a connection string — and a server that knows its failures say such things words them here.</remarks>
    public Func<string, Exception, string>? Error { get; set; }
}

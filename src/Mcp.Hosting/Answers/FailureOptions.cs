namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>The words <c>ExplainFailures</c> answers a failure nobody foresaw with — its error, its hint and its type — each from the tool's name and what it threw.</summary>
/// <remarks>
/// A refusal thrown as <see cref="ToolRefusalException"/> is answered in its own words and never comes here. These are for
/// the rest, and for a server that throws exceptions of its own for a caller's mistake: it tells them by their type and
/// words them as the refusal they are — <c>Type = (_, failure) =&gt; failure is NotLoadedException ? "notLoaded" : null</c>
/// with a hint to match — where "the server failed inside, try once more" contradicted "call load_solution first"
/// (Snail.MCP.Roslyn, Snail.MCP.CodeContext).
/// </remarks>
public sealed class FailureOptions
{
    /// <summary>The error from the tool's name and what it threw; null answers with the message of what it threw.</summary>
    /// <remarks>A message may say more than a model should read — a path, a connection string — and a server that knows its failures say such things words them here.</remarks>
    public Func<string, Exception, string>? Error { get; set; }

    /// <summary>What to do about it, from the tool's name and what it threw; by default, that the server failed inside, to try once more, and to report it if it fails again.</summary>
    /// <remarks>Null, or null from the function, answers with no hint. A fixed hint is <c>(_, _) =&gt; "…"</c>.</remarks>
    public Func<string, Exception, string?>? Hint { get; set; } = static (_, _) => ToolFailures.Hint;

    /// <summary>The kind of failure, from the tool's name and what it threw; null, or null from the function, answers <c>internal</c>.</summary>
    /// <remarks>Only a failure of the type <c>internal</c> is logged as the server's, with its trace; one the server named otherwise is the caller's, and is logged at debug.</remarks>
    public Func<string, Exception, string?>? Type { get; set; }
}

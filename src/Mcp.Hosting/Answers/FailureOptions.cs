namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>The words <c>ExplainFailures</c> answers a failure nobody foresaw with — its error, its hint and its type — each from the tool's name and what it threw, and the form it answers in.</summary>
/// <remarks>
/// <para>
/// A refusal thrown as <see cref="ToolRefusalException"/> is answered in its own words and never comes here. These are for
/// the rest, and for a server that throws exceptions of its own for a caller's mistake: it names each by its type and
/// words it as the refusal it is — <c>options.Refuse&lt;NotLoadedException&gt;("notLoaded", _ =&gt; "call load_solution first")</c> —
/// where "the server failed inside, try once more" contradicted "call load_solution first" (Snail.MCP.Roslyn,
/// Snail.MCP.CodeContext).
/// </para>
/// <para>
/// A failure is told by the first <see cref="Refuse{TException}"/> its exception is, then by <see cref="Type"/>, and is
/// <c>internal</c> when neither names it.
/// </para>
/// </remarks>
public sealed class FailureOptions
{
    /// <summary>The hint a failure of the type <c>internal</c> gets unless the server words its own.</summary>
    internal static readonly Func<string, Exception, string?> InternalOnly = static (_, _) => ToolFailures.Hint;

    private readonly List<Refusal> _refusals = [];

    /// <summary>The error from the tool's name and what it threw; null answers with the message of what it threw.</summary>
    /// <remarks>A message may say more than a model should read — a path, a connection string — and a server that knows its failures say such things words them here.</remarks>
    public Func<string, Exception, string>? Error { get; set; }

    /// <summary>What to do about it, from the tool's name and what it threw; by default, for a failure of the type <c>internal</c>, that the server failed inside, to try once more, and to report it if it fails again.</summary>
    /// <remarks>
    /// <para>
    /// The default hint is the server's failure's alone: a failure <see cref="Type"/> names otherwise is the caller's, and
    /// "try once more" would tell the model to repeat it, so it gets no hint unless this function is set. A fixed hint is
    /// <c>(_, _) =&gt; "…"</c>.
    /// </para>
    /// <para>
    /// Set, the function words every failure that no <see cref="Refuse{TException}"/> named, and null from it answers with
    /// no hint; null answers every such failure with no hint (Snail.MCP.Roslyn answers its own so).
    /// </para>
    /// </remarks>
    public Func<string, Exception, string?>? Hint { get; set; } = InternalOnly;

    /// <summary>The kind of failure, from the tool's name and what it threw; null, or null from the function, answers <c>internal</c>.</summary>
    /// <remarks>Only a failure of the type <c>internal</c> is logged as the server's, with its trace; one the server named otherwise is the caller's, and is logged at debug.</remarks>
    public Func<string, Exception, string?>? Type { get; set; }

    /// <summary>Whether a failure is answered as plain text, <c>error — hint</c>, flagged <c>isError</c>, rather than as the envelope, <c>{"ok":false,"error":…}</c>; false by default.</summary>
    /// <remarks>
    /// <para>
    /// For a server whose answers are text, not data: its successes are words a command or a hook reads as they are, and a
    /// failure in JSON among them is a second form for the same reader (Snail.MCP.Memory, whose answers the mem0 commands
    /// and hooks read; Snail.MCP.Plans, whose answers are markdown). Such a server keeps text, and its failures — what it
    /// threw, arguments the MCP SDK could not bind, a <see cref="ToolRefusalException"/>, a step of <c>run_tools</c> that
    /// threw — say the same things in words: the error, then the hint after a dash when there is one.
    /// </para>
    /// <para>
    /// The type is not written, and neither are the details of a refusal: plain text has no field to keep them in. The type
    /// still decides what is logged as the server's own failure.
    /// </para>
    /// </remarks>
    public bool AnswersText { get; set; }

    /// <summary>
    /// Answers an exception of <typeparamref name="TException"/>, or of a type derived from it, as the caller's mistake of
    /// the kind <paramref name="type"/>, with the hint <paramref name="hint"/> words: a server's own exception for a refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The exception's message stays the error, worded by <see cref="Error"/> when it is set; null from
    /// <paramref name="hint"/>, or no <paramref name="hint"/>, answers with no hint. What no refusal names keeps
    /// <see cref="Type"/> and <see cref="Hint"/>, the default hint of an <c>internal</c> failure with it: setting both
    /// functions for a few exceptions of its own lost that hint for the rest, and Snail.MCP.Obsidian kept the default by
    /// hand to give it back.
    /// </para>
    /// <para>
    /// The first refusal an exception is answers it, in the order they were named, so a type derived from another is named
    /// first. Arguments the MCP SDK could not bind are answered <c>arguments</c> before any of these.
    /// </para>
    /// </remarks>
    /// <typeparam name="TException">The exception the server throws for the refusal.</typeparam>
    /// <param name="type">The kind of refusal, a word a program can branch on: <c>refused</c>, <c>notLoaded</c>.</param>
    /// <param name="hint">What to do about it, from what was thrown.</param>
    /// <returns>These options, for the next refusal.</returns>
    public FailureOptions Refuse<TException>(string type, Func<TException, string?>? hint = null)
        where TException : Exception
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        _refusals.Add(new Refusal(static failure => failure is TException, type, hint is null ? null : failure => hint((TException)failure)));

        return this;
    }

    /// <summary>The refusal the server named for what was thrown, or null when it named none.</summary>
    internal Refusal? RefusalOf(Exception failure) => _refusals.Find(refusal => refusal.Matches(failure));

    /// <summary>A server's own exception answered as a refusal: which exceptions, of what kind, with what hint.</summary>
    internal sealed record Refusal(Func<Exception, bool> Matches, string Type, Func<Exception, string?>? Hint);
}

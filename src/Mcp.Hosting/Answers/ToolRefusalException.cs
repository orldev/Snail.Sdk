using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A call refused for the caller's own mistake, thrown from deep inside a tool: <c>ExplainFailures</c> answers it as it is, its error, hint and type, never as a failure of the server.</summary>
/// <remarks>
/// <para>
/// What a caller can act on comes back as an <see cref="Answer{T}"/>; this is for the refusal that a whole layer would
/// have to carry for one check — the solution the session has not loaded, the codebase nobody indexed — and that the
/// server threw as an <see cref="InvalidOperationException"/>. <c>ExplainFailures</c> answered that "failed inside the
/// server, try once more", which contradicts "call load_solution first" (Snail.MCP.Roslyn, Snail.MCP.CodeContext). A
/// refusal is not logged as the server's failure.
/// </para>
/// <para>
/// A server that throws exceptions of its own for such refusals, and keeps them, words them instead through
/// <see cref="FailureOptions.Hint"/> and <see cref="FailureOptions.Type"/>.
/// </para>
/// </remarks>
public sealed class ToolRefusalException : Exception
{
    /// <summary>A refusal and why, in words the model can act on, with what to do about it and its kind.</summary>
    /// <param name="error">Why the call was refused, in words the model can act on.</param>
    /// <param name="hint">What to do about it: <c>call load_solution first</c>.</param>
    /// <param name="type">The kind of refusal, a word a program can branch on: <c>notLoaded</c>.</param>
    public ToolRefusalException(string error, string? hint = null, string? type = null)
        : base(error ?? throw new ArgumentNullException(nameof(error)))
    {
        Hint = hint;
        Type = type;
    }

    /// <summary>A refusal and why, with no hint and no kind.</summary>
    public ToolRefusalException()
        : this("The call was refused.")
    {
    }

    /// <summary>A refusal and why, with the failure that showed it, which the answer does not quote.</summary>
    /// <param name="error">Why the call was refused, in words the model can act on.</param>
    /// <param name="innerException">The failure that showed the call to be at fault.</param>
    public ToolRefusalException(string error, Exception? innerException)
        : base(error ?? throw new ArgumentNullException(nameof(error)), innerException)
    {
    }

    /// <summary>Why the call was refused: the error of the answer.</summary>
    public string Error => Message;

    /// <summary>What to do about it, when there is something to do.</summary>
    public string? Hint { get; }

    /// <summary>The kind of refusal, a word a program can branch on; left out of the answer when null.</summary>
    public string? Type { get; }

    /// <summary>What the work learned before it was refused, answered as the failure's <c>data</c>.</summary>
    public JsonNode? Details { get; init; }
}

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A value, or why there is none in words a caller can act on.</summary>
/// <remarks>
/// Failure travels as data up to the tool, which turns it into the envelope with <see cref="ToolAnswer.From{T}(Answer{T}, Func{T, System.Text.Json.Nodes.JsonNode}, AnswerShape)"/>.
/// Success is <see cref="Error"/> being null, never <see cref="Value"/> being set: the value of a value type is never
/// null, so <c>Value is { }</c> holds for a failed <c>Answer&lt;bool&gt;</c> too and answered it as <c>false</c>
/// (Snail.MCP.BuildBridge had that trap).
/// </remarks>
/// <param name="Value">What was asked for, when it was had.</param>
/// <param name="Error">Why it was not, in words the model can act on; null on success.</param>
/// <param name="Hint">What to do about it, when there is something to do.</param>
/// <param name="Type">The kind of failure, a word a program can branch on, such as <c>timeout</c>.</param>
public sealed record Answer<T>(T? Value, string? Error, string? Hint, string? Type = null)
{
    /// <summary>Whether the value was had; <see cref="Error"/> says why not otherwise.</summary>
    public bool IsSuccess => Error is null;
}

/// <summary>Makes answers.</summary>
public static class Answer
{
    /// <summary>An answer carrying its value.</summary>
    public static Answer<T> Ok<T>(T value) => new(value, null, null);

    /// <summary>No value, and why.</summary>
    public static Answer<T> Failed<T>(string error, string? hint = null, string? type = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new(default, error, hint, type);
    }
}

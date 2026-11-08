using System.Diagnostics.CodeAnalysis;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A value, or why there is none in words a caller can act on.</summary>
/// <remarks>
/// <para>
/// Failure travels as data up to the tool, which turns it into the envelope with <see cref="ToolAnswer.From{T}(Answer{T}, Func{T, System.Text.Json.Nodes.JsonNode}, AnswerShape)"/>,
/// or up to a job, which then ends failed (<c>Jobs.Start</c> with work that answers an <c>Answer&lt;T&gt;</c>).
/// Success is <see cref="Error"/> being null, never <see cref="Value"/> being set: the value of a value type is never
/// null, so <c>Value is { }</c> holds for a failed <c>Answer&lt;bool&gt;</c> too and answered it as <c>false</c>
/// (Snail.MCP.BuildBridge had that trap).
/// </para>
/// <para>
/// <see cref="IsSuccess"/> tells the compiler that a failure has its <see cref="Error"/>. Nothing tells it that a success
/// has its <see cref="Value"/>, because that is not so for every <typeparamref name="T"/>: an <c>Answer&lt;string?&gt;</c>
/// may succeed with null, and the attribute would make the compiler vouch for a null. <see cref="TryGet"/> hands the value
/// over as <typeparamref name="T"/> itself, exactly as nullable as <typeparamref name="T"/> is, so a caller writes no
/// <c>Value!</c> after the check.
/// </para>
/// </remarks>
/// <param name="Value">What was asked for, when it was had.</param>
/// <param name="Error">Why it was not, in words the model can act on; null on success.</param>
/// <param name="Hint">What to do about it, when there is something to do.</param>
/// <param name="Type">The kind of failure, a word a program can branch on, such as <c>timeout</c>.</param>
public sealed record Answer<T>(T? Value, string? Error, string? Hint, string? Type = null)
{
    /// <summary>Whether the value was had; <see cref="Error"/> says why not otherwise.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>The value, when the answer is a success: <c>if (!answer.TryGet(out var value)) return ToolAnswer.Failed(answer);</c>.</summary>
    /// <param name="value">The value on success, as nullable as <typeparamref name="T"/> is; the default otherwise.</param>
    /// <returns>Whether the answer is a success; when it is not, <see cref="Error"/> says why.</returns>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool TryGet([MaybeNullWhen(false)] out T value)
    {
        value = Value!;

        return IsSuccess;
    }

    /// <summary>The same failure as an answer of another type, its error, hint and type kept: a failure handed on past a layer that answers something else.</summary>
    /// <remarks>Copied by hand, the type was the field left behind (Snail.MCP.BuildBridge, 17 places).</remarks>
    /// <exception cref="InvalidOperationException">The answer is a success, which has a value to carry rather than a failure.</exception>
    public Answer<TOther> As<TOther>() =>
        IsSuccess ? throw new InvalidOperationException(SuccessHasNoFailure) : new(default, Error, Hint, Type);

    /// <summary>The value made into another through <paramref name="map"/>, or the same failure with its error, hint and type.</summary>
    /// <remarks>How work that answers a domain value ends as the data a tool or a job gives: <c>(await runs.RunAsync(…)).Map(AnswerFields.Of)</c>.</remarks>
    public Answer<TOther> Map<TOther>(Func<T, TOther> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return TryGet(out var value) ? new(map(value), null, null) : As<TOther>();
    }

    private const string SuccessHasNoFailure = "A successful answer carries its value, not a failure: only a failure goes on as an answer of another type.";
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

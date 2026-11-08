using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A value, or why there is none in words a caller can act on.</summary>
/// <remarks>
/// <para>
/// Failure travels as data up to the tool, which turns it into the envelope with <see cref="ToolAnswer.From{T}(Answer{T}, Func{T, System.Text.Json.Nodes.JsonNode})"/>,
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
/// <para>
/// A failure may carry what the work learned before it stopped as its <see cref="Details"/> — the kind of refusal a page
/// gave and when to come back, the steps of a sequence that ran and that nothing takes back — which the tool answers as
/// the failure's <c>data</c>. They are JSON rather than a <typeparamref name="T"/>, since what was learned on the way is
/// seldom the shape of what was asked for, and so they go on with the failure into an answer of any other type.
/// </para>
/// <para>
/// A failure needs no type argument: <c>Answer.Failed("…")</c> is an <see cref="AnswerFailure"/>, which becomes an answer
/// of whatever type is expected — a return value, a branch of <c>?:</c> beside <c>Answer.Ok(value)</c>.
/// </para>
/// </remarks>
/// <param name="Value">What was asked for, when it was had.</param>
/// <param name="Error">Why it was not, in words the model can act on; null on success.</param>
public sealed record Answer<T>(T? Value, string? Error)
{
    /// <summary>What to do about the failure, when there is something to do.</summary>
    /// <remarks>Not a positional parameter: beside <see cref="Error"/> and <see cref="Type"/>, three strings in a row were three a caller could put in each other's place, and the compiler would not have said so.</remarks>
    public string? Hint { get; init; }

    /// <summary>The kind of failure, a word a program can branch on, such as <c>timeout</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Whether the value was had; <see cref="Error"/> says why not otherwise.</summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>What a failure learned before it stopped, answered as its <c>data</c>; null when it learned nothing worth saying, and never read on a success.</summary>
    /// <remarks>Snail.MCP.Playwright kept a failed answer's value for this, and Snail.MCP.Git a record beside the answer.</remarks>
    public JsonNode? Details { get; init; }

    /// <summary>The details as the layer that failed holds them, mapped to JSON by the tool that answers: what <see cref="WithDetails{TDetails}"/> carries.</summary>
    internal object? TypedDetails { get; init; }

    /// <summary>The same failure carrying <paramref name="details"/> as they are, for the tool to write as the failure's <c>data</c>: <c>ToolAnswer.From(answer, data, details)</c>.</summary>
    /// <remarks>
    /// <para>
    /// A domain layer knows what it learned — the record a new one would duplicate — and the tool knows how such a record
    /// is written, since the JSON mapper lives with the tools. <see cref="Details"/> is JSON already, so a layer that
    /// wanted to say more than words was handed the mapper as an argument (Snail.MCP.Memory passed <c>duplicateOf</c> down to
    /// its <c>RecordLifecycle</c>). Here it hands on the value, and <see cref="ToolAnswer.From{T, TDetails}(Answer{T}, Func{T, JsonNode}, Func{TDetails, JsonNode})"/>
    /// writes it; a test of the layer reads it back with <see cref="TryGetDetails{TDetails}"/>.
    /// </para>
    /// <para>
    /// The value goes on with the failure through <see cref="As{TOther}"/> and <see cref="Map{TOther}"/>. Only a tool that maps
    /// it writes it: <see cref="ToolAnswer.From{T}(Answer{T}, Func{T, JsonNode})"/>, <see cref="ToolAnswer.Failed{T}(Answer{T})"/>
    /// and a failed job answer the JSON <see cref="Details"/> alone, since nothing there knows how to write the value. A
    /// <see cref="JsonNode"/> is JSON already and becomes <see cref="Details"/> itself.
    /// </para>
    /// </remarks>
    /// <param name="details">What the work learned before it stopped, as the layer holds it.</param>
    /// <exception cref="InvalidOperationException">The answer is a success, whose details are never read.</exception>
    public Answer<T> WithDetails<TDetails>(TDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        if (IsSuccess)
            throw new InvalidOperationException(SuccessHasNoFailure);

        return details is JsonNode node ? this with { Details = node, TypedDetails = null } : this with { Details = null, TypedDetails = details };
    }

    /// <summary>The details a failure carries as the layer that failed gave them (<see cref="WithDetails{TDetails}"/>), when they are a <typeparamref name="TDetails"/>.</summary>
    /// <param name="details">The details when they are a <typeparamref name="TDetails"/>; the default otherwise.</param>
    /// <returns>Whether the failure carries details of that type.</returns>
    public bool TryGetDetails<TDetails>([MaybeNullWhen(false)] out TDetails details)
    {
        if (TypedDetails is TDetails typed)
        {
            details = typed;

            return true;
        }

        details = default;

        return false;
    }

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
        IsSuccess ? throw new InvalidOperationException(SuccessHasNoFailure) : new(default, Error) { Hint = Hint, Type = Type, Details = Details, TypedDetails = TypedDetails };

    /// <summary>The value made into another through <paramref name="map"/>, or the same failure with its error, hint, type and details.</summary>
    /// <remarks>How work that answers a domain value ends as the data a tool or a job gives: <c>(await runs.RunAsync(…)).Map(AnswerFields.Of)</c>.</remarks>
    public Answer<TOther> Map<TOther>(Func<T, TOther> map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return TryGet(out var value) ? new(map(value), null) : As<TOther>();
    }

    /// <summary>The failure as an answer of this type: what lets <c>Answer.Failed("…")</c> be returned where an <c>Answer&lt;T&gt;</c> is expected.</summary>
    [SuppressMessage("Usage", "CA2225:Operator overloads have named alternates", Justification = "The named alternate is Answer.Failed<T>, and AnswerFailure.As<T>() beside it.")]
    public static implicit operator Answer<T>(AnswerFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.As<T>();
    }

    private const string SuccessHasNoFailure = "A successful answer carries its value, not a failure: only a failure goes on as an answer of another type.";
}

/// <summary>Makes answers.</summary>
public static class Answer
{
    /// <summary>An answer carrying its value.</summary>
    public static Answer<T> Ok<T>(T value) => new(value, null);

    /// <summary>No value, and why, as an answer of a type named here: where nothing else names it — <c>var</c>, <c>Task.FromResult</c>.</summary>
    /// <param name="error">Why there is no value, in words the model can act on.</param>
    /// <param name="hint">What to do about it.</param>
    /// <param name="type">The kind of failure, a word a program can branch on.</param>
    /// <param name="details">What the work learned before it stopped, answered as the failure's <c>data</c>.</param>
    public static Answer<T> Failed<T>(string error, string? hint = null, string? type = null, JsonNode? details = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new(default, error) { Hint = hint, Type = type, Details = details };
    }

    /// <summary>No value, and why, as an answer of whatever type is expected: <c>return Answer.Failed("…");</c>, <c>ok ? Answer.Ok(value) : Answer.Failed("…")</c>.</summary>
    /// <remarks>
    /// The type argument of <see cref="Failed{T}(string, string?, string?, JsonNode?)"/> told the compiler nothing it did not
    /// know from the return type, and Snail.MCP.CleanUp wrote it 28 times, Snail.MCP.Playwright 25.
    /// </remarks>
    /// <param name="error">Why there is no value, in words the model can act on.</param>
    /// <param name="hint">What to do about it.</param>
    /// <param name="type">The kind of failure, a word a program can branch on.</param>
    /// <param name="details">What the work learned before it stopped, answered as the failure's <c>data</c>.</param>
    public static AnswerFailure Failed(string error, string? hint = null, string? type = null, JsonNode? details = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new(error) { Hint = hint, Type = type, Details = details };
    }
}

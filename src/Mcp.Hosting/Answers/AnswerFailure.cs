using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A failure not yet of any answer's type: what <see cref="Answer.Failed(string, string?, string?, JsonNode?)"/> makes, and what becomes the <see cref="Answer{T}"/> expected where it is returned.</summary>
/// <param name="Error">Why there is no value, in words the model can act on.</param>
public sealed record AnswerFailure(string Error)
{
    /// <summary>What to do about it, when there is something to do.</summary>
    /// <remarks>Not a positional parameter, as for <see cref="Answer{T}.Hint"/>: three strings in a row were three a caller could misorder.</remarks>
    public string? Hint { get; init; }

    /// <summary>The kind of failure, a word a program can branch on, such as <c>timeout</c>.</summary>
    public string? Type { get; init; }

    /// <summary>What the work learned before it stopped, answered as the failure's <c>data</c>.</summary>
    public JsonNode? Details { get; init; }

    /// <summary>The details as the layer that failed holds them: what <see cref="WithDetails{TDetails}"/> carries.</summary>
    internal object? TypedDetails { get; init; }

    /// <summary>The same failure carrying <paramref name="details"/> as they are, for the tool to write as its <c>data</c>: <c>return Answer.Failed("…").WithDetails(duplicate);</c>.</summary>
    /// <remarks>What <see cref="Answer{T}.WithDetails{TDetails}"/> is for an answer of a type: a domain layer hands on what it learned, and the tool that knows how to write it maps it.</remarks>
    /// <param name="details">What the work learned before it stopped, as the layer holds it; a <see cref="JsonNode"/> becomes <see cref="Details"/> itself.</param>
    public AnswerFailure WithDetails<TDetails>(TDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        return details is JsonNode node ? this with { Details = node, TypedDetails = null } : this with { Details = null, TypedDetails = details };
    }

    /// <summary>The failure as an answer of <typeparamref name="T"/>, its error, hint, type and details kept: where nothing implies the type — <c>var</c>, an argument of a generic method.</summary>
    public Answer<T> As<T>() => new(default, Error) { Hint = Hint, Type = Type, Details = Details, TypedDetails = TypedDetails };
}

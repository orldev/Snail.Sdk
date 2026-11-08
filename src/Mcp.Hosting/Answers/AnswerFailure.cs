using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A failure not yet of any answer's type: what <see cref="Answer.Failed(string, string?, string?, JsonNode?)"/> makes, and what becomes the <see cref="Answer{T}"/> expected where it is returned.</summary>
/// <param name="Error">Why there is no value, in words the model can act on.</param>
/// <param name="Hint">What to do about it, when there is something to do.</param>
/// <param name="Type">The kind of failure, a word a program can branch on, such as <c>timeout</c>.</param>
public sealed record AnswerFailure(string Error, string? Hint = null, string? Type = null)
{
    /// <summary>What the work learned before it stopped, answered as the failure's <c>data</c>.</summary>
    public JsonNode? Details { get; init; }

    /// <summary>The failure as an answer of <typeparamref name="T"/>, its error, hint, type and details kept: where nothing implies the type — <c>var</c>, an argument of a generic method.</summary>
    public Answer<T> As<T>() => new(default, Error, Hint, Type) { Details = Details };
}

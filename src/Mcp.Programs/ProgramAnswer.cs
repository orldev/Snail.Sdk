using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>What one command answered a program: its data, or why it failed in words the program can act on.</summary>
/// <remarks>
/// A failure reaches the program as an exception it may catch: <c>{ error, message, hint, type, details }</c>, where
/// <c>message</c> repeats <c>error</c> because that is where a JavaScript programmer reads an error. Success is
/// <see cref="Error"/> being null, as for <see cref="Answer{T}"/>.
/// </remarks>
/// <param name="Data">What the command answered; null when it answered nothing.</param>
/// <param name="Error">Why it failed; null on success.</param>
/// <param name="Hint">What to do about it.</param>
/// <param name="Type">The kind of failure, a word a program can branch on.</param>
/// <param name="Details">What there is to report all the same.</param>
public sealed record ProgramAnswer(JsonNode? Data, string? Error = null, string? Hint = null, string? Type = null, JsonNode? Details = null)
{
    /// <summary>Whether the command went through; <see cref="Error"/> says why not otherwise.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>An answer carrying the command's data.</summary>
    public static ProgramAnswer Ok(JsonNode? data) => new(data);

    /// <summary>No data, and why.</summary>
    public static ProgramAnswer Failed(string error, string? hint = null, string? type = null, JsonNode? details = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new(null, error, hint, type, details);
    }

    /// <summary>A tool's result as a program reads it: the data of the envelope, or its failure.</summary>
    /// <remarks>
    /// A result that is not the envelope is read as well as it can be: its structured content, else its text as JSON, else
    /// its text as a string. A result flagged <c>isError</c> without the envelope — a refusal of the door — fails with its text.
    /// </remarks>
    public static ProgramAnswer From(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        var parsed = Parsed(text);

        if (ToolAnswer.IsFailure(result) && parsed is JsonObject failure)
            return Failed(Text(failure, "error") ?? "the tool failed", Text(failure, "hint"), Text(failure, "type"), failure["data"]?.DeepClone());

        if (result.IsError == true)
            return Failed(text.Length > 0 ? text : "the tool failed");

        if (parsed is JsonObject envelope && envelope["ok"]?.GetValueKind() == JsonValueKind.True)
            return Ok(envelope["data"]?.DeepClone());

        if (result.StructuredContent is { } structured)
            return Ok(JsonNode.Parse(structured.GetRawText()));

        return Ok(parsed ?? JsonValue.Create(text));
    }

    private static string? Text(JsonObject envelope, string name) =>
        envelope[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonNode? Parsed(string text)
    {
        try
        {
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

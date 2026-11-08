using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>What a test reads from a tool's answer.</summary>
/// <remarks>
/// Every Snail server answers with one text block holding the envelope of <c>ToolAnswer</c> in Snail.Sdk.Mcp.Hosting,
/// <c>{"ok":true,"data":…}</c> or <c>{"ok":false,"error":…,"hint":…,"type":…,"data":…}</c>, a field without a value left
/// out; these read its fields, so a test of the fleet no longer parses the text itself. A text that is not the envelope
/// fails the test with the text in the message. A picture an answer carries follows the envelope in a block of its own
/// (<c>ToolAnswer.WithImage</c>), and <c>Images()</c> reads it.
/// </remarks>
public static class ToolAnswers
{
    extension(CallToolResult answer)
    {
        /// <summary>The text of a tool's answer: the text block it opens with, which holds the envelope in every Snail server, the pictures after it left aside.</summary>
        /// <exception cref="InvalidOperationException">The answer does not open with a text block, or has another text block after it.</exception>
        public string Text()
        {
            ArgumentNullException.ThrowIfNull(answer);

            if (answer.Content is [TextContentBlock block, ..] && answer.Content.Skip(1).All(after => after is not TextContentBlock))
                return block.Text;

            throw new InvalidOperationException($"expected one text block, then pictures if any; got {string.Join(", ", answer.Content.Select(content => content.Type))}");
        }

        /// <summary>The pictures that follow the envelope, in their order; none when the answer carries none.</summary>
        public IReadOnlyList<ImageContentBlock> Images()
        {
            ArgumentNullException.ThrowIfNull(answer);

            return [.. answer.Content.OfType<ImageContentBlock>()];
        }

        /// <summary>Whether the envelope says the call went through.</summary>
        /// <exception cref="InvalidOperationException">The text is not the envelope.</exception>
        public bool IsOk() => EnvelopeOf(answer)["ok"]!.GetValue<bool>();

        /// <summary>The data of the envelope: what a success answers, or what a failure reports all the same; null when it has none.</summary>
        /// <exception cref="InvalidOperationException">The text is not the envelope.</exception>
        public JsonNode? Data() => EnvelopeOf(answer)["data"]?.DeepClone();

        /// <summary>Why the call did not go through; null on success.</summary>
        /// <exception cref="InvalidOperationException">The text is not the envelope.</exception>
        public string? Error() => EnvelopeOf(answer)["error"]?.GetValue<string>();

        /// <summary>What the failure says to do about it; null when it says nothing.</summary>
        /// <exception cref="InvalidOperationException">The text is not the envelope.</exception>
        public string? Hint() => EnvelopeOf(answer)["hint"]?.GetValue<string>();

        /// <summary>The kind of failure, the word a program branches on — <c>internal</c>, <c>arguments</c>, a server's own; null when it names none.</summary>
        /// <exception cref="InvalidOperationException">The text is not the envelope.</exception>
        public string? Type() => EnvelopeOf(answer)["type"]?.GetValue<string>();

        /// <summary>A field the envelope carries at its top level beside <c>data</c> — <c>fileKey</c>, <c>metrics</c>, as <c>ToolAnswer.Ok(data, shape, beside)</c> writes them; null when it has none of that name.</summary>
        /// <param name="name">The field's name; one of the envelope's own — <c>ok</c>, <c>data</c> — reads that too.</param>
        /// <exception cref="InvalidOperationException">The text is not the envelope.</exception>
        public JsonNode? Field(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            return EnvelopeOf(answer)[name]?.DeepClone();
        }

        /// <summary>The job's report the answer carries as its structured content, or null when it carries none.</summary>
        /// <remarks>Where <c>JobAnswer</c> puts it, and where a client of <c>Snail.Sdk.Mcp.JobClient</c> reads it.</remarks>
        public JobReport? Report()
        {
            ArgumentNullException.ThrowIfNull(answer);

            return answer.StructuredContent is { ValueKind: JsonValueKind.Object } structured && structured.TryGetProperty("uri", out _) && structured.TryGetProperty("state", out _)
                ? JobReport.FromJson(structured.GetRawText())
                : null;
        }
    }

    private static JsonObject EnvelopeOf(CallToolResult answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        var text = answer.Text();

        if (Parsed(text) is { } envelope && envelope["ok"]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            return envelope;

        throw new InvalidOperationException($"expected the answer envelope, {{\"ok\":…}}, got: {text}");
    }

    private static JsonObject? Parsed(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

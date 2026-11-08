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
/// fails the test with the text in the message.
/// </remarks>
public static class ToolAnswers
{
    extension(CallToolResult answer)
    {
        /// <summary>The text of a tool's answer, which is one text block in every Snail server.</summary>
        /// <exception cref="InvalidOperationException">The answer is not exactly one text block.</exception>
        public string Text()
        {
            ArgumentNullException.ThrowIfNull(answer);

            return answer.Content is [TextContentBlock block] ? block.Text : throw new InvalidOperationException($"expected one text block, got {answer.Content.Count} blocks");
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

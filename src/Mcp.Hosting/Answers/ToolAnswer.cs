using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A tool's answer as every Snail server gives it: one text block holding the envelope, with an ok flag, and the structured content beside it.</summary>
/// <remarks>
/// <para>
/// The text is compact JSON, <c>{"ok":true,"data":…}</c> on success and <c>{"ok":false,"error":"…","hint":"…","type":"…","data":…}</c>
/// on failure, <c>ok</c> always first. A model reads the shape it was given last, and a refusal shaped differently from an
/// answer is one it misreads as an answer, so both are the same envelope; the protocol's <c>isError</c> is set on every
/// failure as well, for the client that never looks inside.
/// </para>
/// <para>
/// A field without a value is left out: a null <c>hint</c>, <c>type</c> or <c>data</c> is not written, so every field the
/// model reads says something. Fields inside the data are the server's, written as given.
/// </para>
/// <para>
/// A failure carries no structured content. A refusal is not the shape of what the tool answers, and a client of the
/// TypeScript SDK validates the structured content of an error result against the tool's output schema whenever it is
/// there — Snail.MCP.FileSystem sends a refusal as text alone for that reason. The text holds everything a failure says.
/// </para>
/// <para>
/// Nothing is escaped that JSON does not require: the answer is read by a model, which pays for every escape, and the
/// default encoder turned 59 characters of C# with generics and Cyrillic into 154 (Snail.MCP.FileSystem).
/// </para>
/// </remarks>
public static class ToolAnswer
{
    private static readonly AnswerJson Generated = Build();

    private static readonly JsonWriterOptions Writing = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The options answers are written with: compact, nothing escaped that JSON does not require, the JSON nodes generated.</summary>
    /// <remarks>For a server that writes a node of its own the way answers are written: <c>node.ToJsonString(ToolAnswer.Wire)</c>.</remarks>
    public static JsonSerializerOptions Wire => Generated.Options;

    /// <summary>The generated type information of a JsonObject, written as answers are: the result type of a job whose work ends with an answer's data.</summary>
    /// <remarks><c>jobs.Start(kind, work, ToolAnswer.ObjectInfo)</c>, where a server kept a context of its own only for this.</remarks>
    public static JsonTypeInfo<JsonObject> ObjectInfo => Generated.JsonObject;

    /// <summary>A successful answer: the data in the envelope as the text, and the structured content <paramref name="shape"/> names.</summary>
    /// <remarks>
    /// The protocol allows only an object as structured content, so with <see cref="AnswerShape.Data"/> data that is an
    /// array or a value goes as text alone.
    /// </remarks>
    public static CallToolResult Ok(JsonNode data, AnswerShape shape = AnswerShape.Data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var text = Envelope(true, data, null, null, null);
        var structured = shape switch
        {
            AnswerShape.Data when data is JsonObject => JsonSerializer.SerializeToElement(data, Generated.JsonNode),
            AnswerShape.Envelope => JsonSerializer.Deserialize(text, Generated.JsonElement),
            _ => (JsonElement?)null,
        };

        return new CallToolResult { Content = [new TextContentBlock { Text = text }], StructuredContent = structured };
    }

    /// <summary>A failed answer: why, what to do, the kind of failure and what there is to report, flagged as an error, as text alone.</summary>
    /// <param name="error">Why nothing happened, in words the model can act on.</param>
    /// <param name="hint">What to do about it.</param>
    /// <param name="type">The kind of failure, a word a program can branch on.</param>
    /// <param name="data">What there is to report all the same: the items of a batch that went through, what ran before the failure.</param>
    public static CallToolResult Failed(string error, string? hint = null, string? type = null, JsonNode? data = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = Envelope(false, data, error, hint, type) }] };
    }

    /// <summary>An answer as a tool result: its value through <paramref name="data"/>, or its failure.</summary>
    public static CallToolResult From<T>(Answer<T> answer, Func<T, JsonNode> data, AnswerShape shape = AnswerShape.Data)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(data);

        return answer.IsSuccess ? Ok(data(answer.Value!), shape) : Failed(answer.Error!, answer.Hint, answer.Type);
    }

    /// <summary>Whether a result is the failure envelope: one text block whose top-level <c>ok</c> is false.</summary>
    /// <remarks>
    /// <para>
    /// The envelope is read, not parsed: only a text that opens with <c>{</c> is looked at, and the reader stops at the
    /// first top-level <c>ok</c>, which the envelope writes first, so a success of megabytes costs one property rather than
    /// a document tree. A refusal, which is short, is read to its end, so a text cut after <c>"ok":false</c> is not one.
    /// </para>
    /// <para>
    /// Anything else — several blocks, a picture, a text that is not JSON or not the envelope, an <c>ok</c> nested deeper,
    /// malformed JSON — is not a failure: a tool may answer with a report of its own that carries no envelope.
    /// </para>
    /// </remarks>
    public static bool IsFailure(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Content is [TextContentBlock { Text: var text }] && text.StartsWith('{') && IsRefusal(text);
    }

    /// <summary>An enum value as the answer spells it, in the casing the server chose.</summary>
    public static string Word<TEnum>(TEnum value, WordCase casing = WordCase.Lower) where TEnum : struct, Enum
    {
        var name = value.ToString();

        return casing == WordCase.Camel ? JsonNamingPolicy.CamelCase.ConvertName(name) : name.ToLowerInvariant();
    }

    /// <summary>A moment as the answer spells it: round-trip ISO 8601 with its offset, the same on every machine.</summary>
    public static string Moment(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);

    private static AnswerJson Build()
    {
        var generated = new AnswerJson(new JsonSerializerOptions { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        generated.Options.MakeReadOnly();

        return generated;
    }

    private static string Envelope(bool isOk, JsonNode? data, string? error, string? hint, string? type)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, Writing))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ok"u8, isOk);
            WriteIfAny(writer, "error"u8, error);
            WriteIfAny(writer, "hint"u8, hint);
            WriteIfAny(writer, "type"u8, type);

            if (data is not null)
            {
                writer.WritePropertyName("data"u8);
                data.WriteTo(writer, Wire);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteIfAny(Utf8JsonWriter writer, ReadOnlySpan<byte> name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }

    /// <summary>Whether the rest of the text is well-formed JSON; it throws on the first token that is not.</summary>
    private static bool IsWhole(ref Utf8JsonReader reader)
    {
        while (reader.Read())
        {
        }

        return true;
    }

    private static bool IsRefusal(string envelope)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(envelope));

        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("ok"u8))
                    return reader.Read() && reader.TokenType == JsonTokenType.False && IsWhole(ref reader);

                reader.Skip();
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

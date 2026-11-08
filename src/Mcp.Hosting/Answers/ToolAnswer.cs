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

    /// <summary>How the text of every success opens, its data written next.</summary>
    private static ReadOnlySpan<byte> SuccessOpening => """{"ok":true,"data":"""u8;

    /// <summary>The options answers are written with: compact, nothing escaped that JSON does not require, the JSON nodes generated.</summary>
    /// <remarks>For a server that writes a node of its own the way answers are written: <c>node.ToJsonString(ToolAnswer.Wire)</c>.</remarks>
    public static JsonSerializerOptions Wire => Generated.Options;

    /// <summary>The generated type information of a JsonObject, written as answers are: the result type of a job whose work ends with an answer's data.</summary>
    /// <remarks><c>jobs.Start(kind, work, ToolAnswer.ObjectInfo)</c>, where a server kept a context of its own only for this.</remarks>
    public static JsonTypeInfo<JsonObject> ObjectInfo => Generated.JsonObject;

    /// <summary>The generated type information of a JsonElement, for data kept as JSON already: a job's result.</summary>
    internal static JsonTypeInfo<JsonElement> ElementInfo => Generated.JsonElement;

    /// <summary>A successful answer: the data in the envelope as the text, and the structured content <paramref name="shape"/> names.</summary>
    /// <remarks>
    /// The protocol allows only an object as structured content, so with <see cref="AnswerShape.Data"/> data that is an
    /// array or a value goes as text alone.
    /// </remarks>
    public static CallToolResult Ok(JsonNode data, AnswerShape shape)
    {
        ArgumentNullException.ThrowIfNull(data);

        return Succeeded(writer => data.WriteTo(writer, Wire), shape);
    }

    /// <summary>A successful answer: the data in the envelope as the text, and as the structured content when it is an object.</summary>
    public static CallToolResult Ok(JsonNode data) => Ok(data, AnswerShape.Data);

    /// <summary>A successful answer of typed data, written from its generated type straight into the text: no tree of nodes is built first.</summary>
    /// <remarks>
    /// <para>
    /// For data that is large or typed already: a run's files as base64 (Snail.MCP.Sandbox sends up to 32 MB), a record
    /// the server keeps a generated context for. The names and converters are those of <paramref name="type"/>; the
    /// escaping is the envelope's, nothing escaped that JSON does not require, whatever the type's options say.
    /// </para>
    /// <para>
    /// The structured content of <see cref="AnswerShape.Data"/> is read from the same bytes the text holds, once, and
    /// left out when the data is not an object, as with <see cref="Ok(JsonNode, AnswerShape)"/>.
    /// </para>
    /// </remarks>
    /// <param name="data">What the tool answers.</param>
    /// <param name="type">Its generated type information, from the server's context: a native build looks nothing up at run time.</param>
    /// <param name="shape">What goes beside the text as the structured content.</param>
    public static CallToolResult Ok<T>(T data, JsonTypeInfo<T> type, AnswerShape shape)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(type);

        return Succeeded(writer => JsonSerializer.Serialize(writer, data, type), shape);
    }

    /// <summary>A successful answer of typed data, written from its generated type straight into the text, with the data as the structured content when it is an object.</summary>
    /// <param name="data">What the tool answers.</param>
    /// <param name="type">Its generated type information, from the server's context.</param>
    public static CallToolResult Ok<T>(T data, JsonTypeInfo<T> type) => Ok(data, type, AnswerShape.Data);

    /// <summary>A failed answer: why, what to do, the kind of failure and what there is to report, flagged as an error, as text alone.</summary>
    /// <param name="error">Why nothing happened, in words the model can act on.</param>
    /// <param name="hint">What to do about it.</param>
    /// <param name="type">The kind of failure, a word a program can branch on.</param>
    /// <param name="data">What there is to report all the same: the items of a batch that went through, what ran before the failure.</param>
    public static CallToolResult Failed(string error, string? hint = null, string? type = null, JsonNode? data = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = Envelope(false, data is null ? null : writer => data.WriteTo(writer, Wire), error, hint, type) }] };
    }

    /// <summary>A failed answer as the tool's answer: its error, hint and type, and what there is to report all the same.</summary>
    /// <remarks>Written out by hand, the type was the field left behind (Snail.MCP.BuildBridge, six places).</remarks>
    /// <exception cref="ArgumentException">The answer is a success, which has a value to answer rather than a failure.</exception>
    public static CallToolResult Failed<T>(Answer<T> failed) => Failed(failed, null);

    /// <summary>A failed answer as the tool's answer, with what there is to report all the same as its data.</summary>
    /// <exception cref="ArgumentException">The answer is a success, which has a value to answer rather than a failure.</exception>
    public static CallToolResult Failed<T>(Answer<T> failed, JsonNode? data)
    {
        ArgumentNullException.ThrowIfNull(failed);

        if (failed.IsSuccess)
            throw new ArgumentException("A successful answer has a value to answer, not a failure: answer it with From or Ok.", nameof(failed));

        return Failed(failed.Error, failed.Hint, failed.Type, data);
    }

    /// <summary>An answer as a tool result: its value through <paramref name="data"/>, or its failure.</summary>
    public static CallToolResult From<T>(Answer<T> answer, Func<T, JsonNode> data, AnswerShape shape = AnswerShape.Data)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(data);

        return answer.TryGet(out var value) ? Ok(data(value), shape) : Failed(answer.Error, answer.Hint, answer.Type);
    }

    /// <summary>An answer whose value is JSON already as a tool result: the value as the data, or its failure.</summary>
    public static CallToolResult From<T>(Answer<T> answer)
        where T : JsonNode => From(answer, AnswerShape.Data);

    /// <summary>An answer whose value is JSON already as a tool result, with the structured content <paramref name="shape"/> names.</summary>
    public static CallToolResult From<T>(Answer<T> answer, AnswerShape shape)
        where T : JsonNode
    {
        ArgumentNullException.ThrowIfNull(answer);

        return answer.TryGet(out var value) ? Ok(value, shape) : Failed(answer.Error, answer.Hint, answer.Type);
    }

    /// <summary>The envelope a result carries: the text of its first block, which is where every answer of <see cref="ToolAnswer"/> writes it.</summary>
    /// <remarks>
    /// For an answer that travels outside a tool result as it is: the body of an HTTP door that refuses in the words a tool
    /// would (Snail.MCP.Connector's dashboard), a record kept of what a tool answered. A result with blocks after the text
    /// — files, pictures — still has the envelope first.
    /// </remarks>
    /// <exception cref="ArgumentException">The result does not open with a text block.</exception>
    public static string Text(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Content is [TextContentBlock { Text: var text }, ..]
            ? text
            : throw new ArgumentException($"A tool answer opens with the text block of its envelope; this one opens with {(result.Content.Count == 0 ? "nothing" : result.Content[0].Type)}.", nameof(result));
    }

    /// <summary>The tool result a stored envelope stands for: a success with its data, or a failure with its error, hint, type and data, flagged as an error.</summary>
    /// <remarks>
    /// The inverse of <see cref="Text"/>: an envelope kept as JSON — in a job's result, a file, a queue — answered again as
    /// the tool first answered it, written by the same rules, so a failure is text alone and <c>isError</c> whatever it
    /// was stored as.
    /// </remarks>
    /// <param name="envelope">The envelope, <c>{"ok":true,"data":…}</c> or <c>{"ok":false,"error":…}</c>.</param>
    /// <param name="shape">What a success carries beside the text as the structured content.</param>
    /// <exception cref="ArgumentException">The object is not the envelope: no <c>ok</c> flag, a success without data or a failure without an error.</exception>
    public static CallToolResult Of(JsonObject envelope, AnswerShape shape = AnswerShape.Data)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var isOk = envelope["ok"]?.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException("An envelope opens with an ok flag, true or false; this object has none.", nameof(envelope)),
        };

        if (isOk)
            return Ok(envelope["data"]?.DeepClone() ?? throw new ArgumentException("A successful envelope carries its data; this one has none.", nameof(envelope)), shape);

        var error = StringOf(envelope["error"]) ?? throw new ArgumentException("A failed envelope says why in its error; this one does not.", nameof(envelope));

        return Failed(error, StringOf(envelope["hint"]), StringOf(envelope["type"]), envelope["data"]?.DeepClone());
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

    /// <summary>A success whose data <paramref name="data"/> writes, with the structured content <paramref name="shape"/> names read from the text's own bytes.</summary>
    private static CallToolResult Succeeded(Action<Utf8JsonWriter> data, AnswerShape shape)
    {
        var buffer = new ArrayBufferWriter<byte>();

        Write(buffer, true, data, null, null, null);

        var written = buffer.WrittenSpan;
        var structured = shape switch
        {
            AnswerShape.Data => DataOf(written),
            AnswerShape.Envelope => Element(written),
            _ => null,
        };

        return new CallToolResult { Content = [new TextContentBlock { Text = Encoding.UTF8.GetString(written) }], StructuredContent = structured };
    }

    /// <summary>The data of a success's envelope as the structured content when it is an object: the bytes between <c>{"ok":true,"data":</c> and the closing brace.</summary>
    private static JsonElement? DataOf(ReadOnlySpan<byte> envelope)
    {
        var data = envelope[SuccessOpening.Length..^1];

        return data is [(byte)'{', ..] ? Element(data) : null;
    }

    private static JsonElement Element(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);

        return JsonElement.ParseValue(ref reader);
    }

    private static string Envelope(bool isOk, Action<Utf8JsonWriter>? data, string? error, string? hint, string? type)
    {
        var buffer = new ArrayBufferWriter<byte>();

        Write(buffer, isOk, data, error, hint, type);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(ArrayBufferWriter<byte> buffer, bool isOk, Action<Utf8JsonWriter>? data, string? error, string? hint, string? type)
    {
        using var writer = new Utf8JsonWriter(buffer, Writing);

        writer.WriteStartObject();
        writer.WriteBoolean("ok"u8, isOk);
        WriteIfAny(writer, "error"u8, error);
        WriteIfAny(writer, "hint"u8, hint);
        WriteIfAny(writer, "type"u8, type);

        if (data is not null)
        {
            writer.WritePropertyName("data"u8);
            data(writer);
        }

        writer.WriteEndObject();
    }

    private static string? StringOf(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

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

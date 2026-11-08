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
/// The text is compact JSON, <c>{"ok":true,"data":…}</c> on success (with a <c>hint</c> before <c>data</c> when the tool gives one) and <c>{"ok":false,"error":"…","hint":"…","type":"…","data":…}</c>
/// on failure, <c>ok</c> always first. A model reads the shape it was given last, and a refusal shaped differently from an
/// answer is one it misreads as an answer, so both are the same envelope; the protocol's <c>isError</c> is set on every
/// failure as well, for the client that never looks inside.
/// </para>
/// <para>
/// A field without a value is left out: a null <c>hint</c>, <c>type</c> or <c>data</c> is not written, so every field the
/// model reads says something. Fields inside the data are the server's, written as given, and so are fields a tool's
/// contract keeps beside it at the top level, written after <c>data</c> (<see cref="Ok(JsonNode, AnswerShape, JsonObject?, string?)"/>).
/// </para>
/// <para>
/// A failure carries no structured content. A refusal is not the shape of what the tool answers, and a client of the
/// TypeScript SDK validates the structured content of an error result against the tool's output schema whenever it is
/// there — Snail.MCP.FileSystem sends a refusal as text alone for that reason. The text holds everything a failure says.
/// </para>
/// <para>
/// A success with structured content carries its data twice: once in the text, once beside it — the protocol asks a
/// server with structured content to repeat it as text for the client that reads text alone. A budget on the size of
/// an answer counts the text, which is what a model is given; counting the whole result counted the data twice
/// (Snail.MCP.Roslyn's filter saw 70 357 characters for one listing). A server whose payload is large and read by a
/// model — the text of a page — answers <see cref="AnswerShape.None"/>, and a client that hands the whole result to its
/// model pays for it once.
/// </para>
/// <para>
/// A picture goes in a block of its own after the text (<see cref="WithImage"/>), never as base64 inside the JSON: a
/// client hands an image block to a model as an image, and megabytes of characters read as characters help nobody.
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

    /// <summary>The generated type information of a JsonElement, for data kept as JSON already: a job's result.</summary>
    internal static JsonTypeInfo<JsonElement> ElementInfo => Generated.JsonElement;

    /// <summary>The generated type information of a JsonNode, for what a failure learned kept as a job's result.</summary>
    internal static JsonTypeInfo<JsonNode> NodeInfo => Generated.JsonNode;

    /// <summary>A successful answer: the data in the envelope as the text, and as the structured content when it is an object.</summary>
    public static CallToolResult Ok(JsonNode data) => Ok(data, AnswerShape.Data);

    /// <summary>A successful answer: the data in the envelope as the text, the structured content <paramref name="shape"/> names, and — when given — fields of the tool's own after <c>data</c> and what to do next.</summary>
    /// <remarks>
    /// <para>
    /// The protocol allows only an object as structured content, so with <see cref="AnswerShape.Data"/> data that is an
    /// array or a value goes as text alone. With <see cref="AnswerShape.Data"/> or <see cref="AnswerShape.Envelope"/> the
    /// data is in the result twice, in the text and beside it: a size budget counts the text.
    /// </para>
    /// <para>
    /// For a tool whose contract reads fields beside the data — the file and the node a read came from, where it came
    /// from and what it saved (Snail.MCP.Figma, 17 tools: <c>fileKey</c>, <c>nodeId</c>, <c>source</c>, <c>fileVersion</c>,
    /// <c>staleReason</c>, <c>metrics</c>) — which the envelope had no place for, so the server wrote it by hand. They are
    /// written in their order after <c>data</c>, by the same options; one whose value is null is left out, as every field
    /// of the envelope without a value is.
    /// </para>
    /// <para>
    /// The structured content of <see cref="AnswerShape.Data"/> is the data alone, without them; that of
    /// <see cref="AnswerShape.Envelope"/> is the text's whole object, with them.
    /// </para>
    /// <para>
    /// A success's <paramref name="hint"/> is the envelope's own field, in its place before <c>data</c> as a failure's is:
    /// what to do next with what was answered — save a version, narrow a search that found too much. A field beside named
    /// <c>hint</c> was refused, so Snail.MCP.Figma wrote the envelope of three tools by hand to keep the hint its contract
    /// reads (<c>figma_save_version</c>, <c>figma_search_components</c>, <c>figma_search_team_components</c>).
    /// </para>
    /// </remarks>
    /// <param name="data">What the tool answers.</param>
    /// <param name="shape">What goes beside the text as the structured content.</param>
    /// <param name="beside">The fields written after <c>data</c>, none when null; none may take a name of the envelope's own.</param>
    /// <param name="hint">What to do next with the answer; left out when null.</param>
    /// <exception cref="ArgumentException">A field beside is named <c>ok</c>, <c>error</c>, <c>hint</c>, <c>type</c> or <c>data</c>.</exception>
    public static CallToolResult Ok(JsonNode data, AnswerShape shape, JsonObject? beside = null, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        RefuseEnvelopeNames(beside);

        return Succeeded(writer => data.WriteTo(writer, Wire), shape, beside, hint);
    }

    /// <summary>The text of a successful answer, <c>{"ok":true,"data":…}</c>, for a tool that returns a string: no structured content, nothing escaped that JSON does not require, and — when given — fields of the tool's own after <c>data</c> and what to do next.</summary>
    /// <remarks>
    /// A tool that returns a string is answered by the MCP SDK as one text block, which is what this is; written out as
    /// <c>ToolAnswer.Text(ToolAnswer.Ok(data, AnswerShape.None))</c>, it was long in every one of the 136 tools of
    /// Snail.MCP.Figma that answer so. <c>ExplainFailures</c> flags the string of a failure <c>isError</c>.
    /// </remarks>
    /// <param name="data">What the tool answers.</param>
    /// <param name="beside">The fields written after <c>data</c>, as <see cref="Ok(JsonNode, AnswerShape, JsonObject?, string?)"/> writes them; none when null.</param>
    /// <param name="hint">What to do next with the answer, the envelope's own field before <c>data</c>; left out when null.</param>
    /// <exception cref="ArgumentException">A field beside is named <c>ok</c>, <c>error</c>, <c>hint</c>, <c>type</c> or <c>data</c>.</exception>
    public static string OkText(JsonNode data, JsonObject? beside = null, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        RefuseEnvelopeNames(beside);

        return Envelope(true, writer => data.WriteTo(writer, Wire), null, hint, null, beside);
    }

    /// <summary>The text of a failed answer, <c>{"ok":false,"error":…}</c>, for a tool that returns a string; <c>ExplainFailures</c> flags it <c>isError</c> on its way out.</summary>
    /// <param name="error">Why nothing happened, in words the model can act on.</param>
    /// <param name="hint">What to do about it.</param>
    /// <param name="type">The kind of failure, a word a program can branch on.</param>
    /// <param name="data">What there is to report all the same.</param>
    public static string FailedText(string error, string? hint = null, string? type = null, JsonNode? data = null)
    {
        ArgumentNullException.ThrowIfNull(error);

        return Envelope(false, data is null ? null : writer => data.WriteTo(writer, Wire), error, hint, type, null);
    }

    /// <summary>A successful answer of typed data, written from its generated type straight into the text: no tree of nodes is built first.</summary>
    /// <remarks>
    /// <para>
    /// For data that is large or typed already: a run's files as base64 (Snail.MCP.Sandbox sends up to 32 MB), a record
    /// the server keeps a generated context for. The names and converters are those of <paramref name="type"/>; the
    /// escaping is the envelope's, nothing escaped that JSON does not require, whatever the type's options say.
    /// </para>
    /// <para>
    /// The structured content of <see cref="AnswerShape.Data"/> is read from the same bytes the text holds, once, and
    /// left out when the data is not an object, as with <see cref="Ok(JsonNode, AnswerShape, JsonObject?, string?)"/>.
    /// </para>
    /// </remarks>
    /// <param name="data">What the tool answers.</param>
    /// <param name="type">Its generated type information, from the server's context: a native build looks nothing up at run time.</param>
    /// <param name="shape">What goes beside the text as the structured content.</param>
    public static CallToolResult Ok<T>(T data, JsonTypeInfo<T> type, AnswerShape shape)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(type);

        return Succeeded(writer => JsonSerializer.Serialize(writer, data, type), shape, null, null);
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

        return Failure(error, hint, type, data, null);
    }

    /// <summary>A failed answer as the tool's answer: its error, hint and type, and its details as the data.</summary>
    /// <remarks>Written out by hand, the type was the field left behind (Snail.MCP.BuildBridge, six places).</remarks>
    /// <exception cref="ArgumentException">The answer is a success, which has a value to answer rather than a failure.</exception>
    public static CallToolResult Failed<T>(Answer<T> failed) => Failed(failed, null);

    /// <summary>A failed answer as the tool's answer, with what there is to report all the same as its data: <paramref name="data"/>, or the answer's details when it is null.</summary>
    /// <exception cref="ArgumentException">The answer is a success, which has a value to answer rather than a failure.</exception>
    public static CallToolResult Failed<T>(Answer<T> failed, JsonNode? data)
    {
        ArgumentNullException.ThrowIfNull(failed);

        if (failed.IsSuccess)
            throw new ArgumentException("A successful answer has a value to answer, not a failure: answer it with From or Ok.", nameof(failed));

        return Failed(failed.Error, failed.Hint, failed.Type, data ?? failed.Details);
    }

    /// <summary>An answer as a tool result: its value through <paramref name="data"/>, or its failure with its details as the data.</summary>
    public static CallToolResult From<T>(Answer<T> answer, Func<T, JsonNode> data) => From(answer, data, AnswerShape.Data);

    /// <summary>An answer as a tool result: its value through <paramref name="data"/> with the structured content <paramref name="shape"/> names, or its failure with its details as the data.</summary>
    public static CallToolResult From<T>(Answer<T> answer, Func<T, JsonNode> data, AnswerShape shape)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(data);

        return answer.TryGet(out var value) ? Ok(data(value), shape) : Failed(answer.Error, answer.Hint, answer.Type, answer.Details);
    }

    /// <summary>An answer as a tool result: its value through <paramref name="data"/>, or its failure with the details its layer handed on written through <paramref name="details"/>.</summary>
    /// <remarks>
    /// The details a domain layer carries as they are (<see cref="Answer{T}.WithDetails{TDetails}"/>) are written here, by
    /// the tool that holds the mapper, rather than by a mapper passed down to the layer (Snail.MCP.Memory's
    /// <c>duplicateOf</c>): <c>ToolAnswer.From&lt;Remembered, ScoredRecord&gt;(answer, AnswerFields.Of, AnswerFields.Of)</c>. A failure
    /// whose details are not a <typeparamref name="TDetails"/> is answered with its JSON <see cref="Answer{T}.Details"/>.
    /// </remarks>
    /// <param name="answer">The answer: a value, or why there is none.</param>
    /// <param name="data">How the value is written.</param>
    /// <param name="details">How the failure's details are written.</param>
    public static CallToolResult From<T, TDetails>(Answer<T> answer, Func<T, JsonNode> data, Func<TDetails, JsonNode> details) =>
        From(answer, data, details, AnswerShape.Data);

    /// <summary>An answer as a tool result: its value through <paramref name="data"/> with the structured content <paramref name="shape"/> names, or its failure with the details its layer handed on written through <paramref name="details"/>.</summary>
    /// <param name="answer">The answer: a value, or why there is none.</param>
    /// <param name="data">How the value is written.</param>
    /// <param name="details">How the failure's details are written.</param>
    /// <param name="shape">The structured content of a success.</param>
    public static CallToolResult From<T, TDetails>(Answer<T> answer, Func<T, JsonNode> data, Func<TDetails, JsonNode> details, AnswerShape shape)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(details);

        if (answer.TryGet(out var value))
            return Ok(data(value), shape);

        var said = answer.TryGetDetails<TDetails>(out var learned) ? details(learned) : answer.Details;

        return Failed(answer.Error, answer.Hint, answer.Type, said);
    }

    /// <summary>An answer of typed data as a tool result: its value written from its generated type, as <see cref="Ok{T}(T, JsonTypeInfo{T})"/> writes it, or its failure with its details as the data.</summary>
    /// <remarks>Where a server mapped its own records to nodes only to hand them to <see cref="From{T}(Answer{T}, Func{T, JsonNode}, AnswerShape)"/> (Snail.MCP.Obsidian).</remarks>
    /// <param name="answer">The answer: a value, or why there is none.</param>
    /// <param name="type">The value's generated type information, from the server's context.</param>
    /// <exception cref="ArgumentNullException">The answer is a success that holds null: there is nothing to answer.</exception>
    public static CallToolResult From<T>(Answer<T> answer, JsonTypeInfo<T> type) => From(answer, type, AnswerShape.Data);

    /// <summary>An answer of typed data as a tool result, with the structured content <paramref name="shape"/> names.</summary>
    /// <param name="answer">The answer: a value, or why there is none.</param>
    /// <param name="type">The value's generated type information, from the server's context.</param>
    /// <param name="shape">What a success carries beside the text as the structured content.</param>
    /// <exception cref="ArgumentNullException">The answer is a success that holds null: there is nothing to answer.</exception>
    public static CallToolResult From<T>(Answer<T> answer, JsonTypeInfo<T> type, AnswerShape shape)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(type);

        return answer.TryGet(out var value) ? Ok(value, type, shape) : Failed(answer.Error, answer.Hint, answer.Type, answer.Details);
    }

    /// <summary>An answer whose value is JSON already as a tool result: the value as the data, or its failure with its details as the data.</summary>
    public static CallToolResult From<T>(Answer<T> answer)
        where T : JsonNode => From(answer, AnswerShape.Data);

    /// <summary>An answer whose value is JSON already as a tool result, with the structured content <paramref name="shape"/> names.</summary>
    public static CallToolResult From<T>(Answer<T> answer, AnswerShape shape)
        where T : JsonNode
    {
        ArgumentNullException.ThrowIfNull(answer);

        return answer.TryGet(out var value) ? Ok(value, shape) : Failed(answer.Error, answer.Hint, answer.Type, answer.Details);
    }

    extension(CallToolResult result)
    {
        /// <summary>The answer with a picture after its text, in an image block of its own: <c>ToolAnswer.Ok(data, AnswerShape.None).WithImage(png, "image/png")</c>.</summary>
        /// <remarks>
        /// <para>
        /// The envelope stays the first block, so <see cref="Text"/> still reads it; the picture is for a model to look at,
        /// never a field of the JSON, which would be megabytes of base64 read as characters. Snail.MCP.Playwright,
        /// AfterEffects, Blender and Figma each added the block by hand.
        /// </para>
        /// <para>
        /// A failure may carry a picture too — what the screen showed when the work stopped — and stays a failure: it is
        /// flagged <c>isError</c> when it is made, whatever follows its text.
        /// </para>
        /// </remarks>
        /// <param name="bytes">The picture as it is encoded: the bytes of a PNG, a JPEG.</param>
        /// <param name="mimeType">What the bytes are: <c>image/png</c>, <c>image/jpeg</c>.</param>
        /// <returns>The same result, the picture added after its last block.</returns>
        public CallToolResult WithImage(ReadOnlyMemory<byte> bytes, string mimeType)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

            if (bytes.IsEmpty)
                throw new ArgumentException("A picture has bytes; these are none.", nameof(bytes));

            result.Content.Add(ImageContentBlock.FromBytes(bytes, mimeType));

            return result;
        }
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
    /// was stored as. Fields beside the envelope's own — written by <see cref="Ok(JsonNode, AnswerShape, JsonObject?, string?)"/>,
    /// or by a server by hand — are kept, after <c>data</c>, a success's and a failure's alike; a null one is left out. A
    /// success's <c>hint</c> is kept as well, where it was dropped.
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

        var beside = BesideOf(envelope);

        if (isOk)
        {
            var data = envelope["data"]?.DeepClone() ?? throw new ArgumentException("A successful envelope carries its data; this one has none.", nameof(envelope));

            return Succeeded(writer => data.WriteTo(writer, Wire), shape, beside, StringOf(envelope["hint"]));
        }

        var error = StringOf(envelope["error"]) ?? throw new ArgumentException("A failed envelope says why in its error; this one does not.", nameof(envelope));

        return Failure(error, StringOf(envelope["hint"]), StringOf(envelope["type"]), envelope["data"]?.DeepClone(), beside);
    }

    /// <summary>Whether a result is the failure envelope: a text block whose top-level <c>ok</c> is false, with nothing after it but pictures.</summary>
    /// <remarks>
    /// <para>
    /// The envelope is read, not parsed: only a text that opens with <c>{</c> is looked at, and the reader stops at the
    /// first top-level <c>ok</c>, which the envelope writes first, so a success of megabytes costs one property rather than
    /// a document tree. A refusal, which is short, is read to its end, so a text cut after <c>"ok":false</c> is not one.
    /// </para>
    /// <para>
    /// Anything else — several text blocks, a picture first, a text that is not JSON or not the envelope, an <c>ok</c>
    /// nested deeper, malformed JSON — is not a failure: a tool may answer with a report of its own that carries no
    /// envelope. Pictures after the envelope (<see cref="WithImage"/>) leave it what it is.
    /// </para>
    /// </remarks>
    public static bool IsFailure(CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Content is [TextContentBlock { Text: var text }, ..]
            && result.Content.Skip(1).All(after => after is ImageContentBlock)
            && text.StartsWith('{')
            && IsRefusal(text);
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
    private static CallToolResult Succeeded(Action<Utf8JsonWriter> data, AnswerShape shape, JsonObject? beside, string? hint)
    {
        var buffer = new ArrayBufferWriter<byte>();

        var dataBytes = Write(buffer, true, data, null, hint, null, beside);

        var written = buffer.WrittenSpan;
        var structured = shape switch
        {
            AnswerShape.Data => DataOf(written[dataBytes]),
            AnswerShape.Envelope => Element(written),
            _ => null,
        };

        return new CallToolResult { Content = [new TextContentBlock { Text = Encoding.UTF8.GetString(written) }], StructuredContent = structured };
    }

    /// <summary>A failure, flagged as an error, as text alone, with the fields beside its data a stored envelope kept.</summary>
    private static CallToolResult Failure(string error, string? hint, string? type, JsonNode? data, JsonObject? beside) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = Envelope(false, data is null ? null : writer => data.WriteTo(writer, Wire), error, hint, type, beside) }] };

    /// <summary>The data of a success's envelope as the structured content when it is an object.</summary>
    private static JsonElement? DataOf(ReadOnlySpan<byte> data) => data is [(byte)'{', ..] ? Element(data) : null;

    private static JsonElement Element(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json);

        return JsonElement.ParseValue(ref reader);
    }

    private static string Envelope(bool isOk, Action<Utf8JsonWriter>? data, string? error, string? hint, string? type, JsonObject? beside)
    {
        var buffer = new ArrayBufferWriter<byte>();

        Write(buffer, isOk, data, error, hint, type, beside);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Writes the envelope, its fields in their order and those beside the data after it.</summary>
    /// <returns>Where the data's own bytes lie in what was written; an empty range when there is no data.</returns>
    private static Range Write(ArrayBufferWriter<byte> buffer, bool isOk, Action<Utf8JsonWriter>? data, string? error, string? hint, string? type, JsonObject? beside)
    {
        using var writer = new Utf8JsonWriter(buffer, Writing);
        var dataBytes = new Range(0, 0);

        writer.WriteStartObject();
        writer.WriteBoolean("ok"u8, isOk);
        WriteIfAny(writer, "error"u8, error);
        WriteIfAny(writer, "hint"u8, hint);
        WriteIfAny(writer, "type"u8, type);

        if (data is not null)
        {
            writer.WritePropertyName("data"u8);
            writer.Flush();

            var start = buffer.WrittenCount;
            data(writer);
            writer.Flush();

            dataBytes = new Range(start, buffer.WrittenCount);
        }

        foreach (var (name, value) in beside ?? [])
        {
            if (value is null)
                continue;

            writer.WritePropertyName(name);
            value.WriteTo(writer, Wire);
        }

        writer.WriteEndObject();

        return dataBytes;
    }

    /// <summary>The fields of a stored envelope that are not the envelope's own, or null when it has none.</summary>
    private static JsonObject? BesideOf(JsonObject envelope)
    {
        JsonObject? beside = null;

        foreach (var (name, value) in envelope)
        {
            if (IsEnvelopeName(name))
                continue;

            beside ??= [];
            beside[name] = value?.DeepClone();
        }

        return beside;
    }

    private static void RefuseEnvelopeNames(JsonObject? beside)
    {
        foreach (var (name, _) in beside ?? [])
        {
            if (IsEnvelopeName(name))
                throw new ArgumentException($"A field beside the data cannot be named '{name}': ok, error, hint, type and data are the envelope's own.", nameof(beside));
        }
    }

    private static bool IsEnvelopeName(string name) => name is "ok" or "error" or "hint" or "type" or "data";

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

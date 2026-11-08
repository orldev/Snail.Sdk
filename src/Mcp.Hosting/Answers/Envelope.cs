using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>The answer envelope as a type: the declared output of a tool whose structured content is the whole envelope, so its schema is published.</summary>
/// <remarks>
/// <para>
/// A tool answering with <see cref="AnswerShape.Envelope"/> states it as its output schema —
/// <c>[McpServerTool(UseStructuredContent = true, OutputSchemaType = typeof(Envelope&lt;FileRead&gt;))]</c> — and a client
/// that validates structured content, or makes a typed call of the tool, reads the shape from it. The names are the
/// envelope's whatever the server's options say, <c>ok</c> first; a field without a value is left out, as
/// <see cref="ToolAnswer"/> leaves it out. Snail.MCP.FileSystem kept a type of its own for this, <c>Answered&lt;T&gt;</c>.
/// </para>
/// <para>
/// A native build serializes it through the server's generated context, which names each closed type it publishes:
/// <c>[JsonSerializable(typeof(Envelope&lt;FileRead&gt;))]</c>.
/// </para>
/// <para>
/// The schema is a success's: a failure carries no structured content, so <c>data</c> is published as required and never
/// null — <c>{"type":"object"}</c>, not <c>["object","null"]</c> — and a client that validates it against a success finds
/// it there. That is why <typeparamref name="T"/> is <c>notnull</c>: a nullable data let the published schema say a success
/// may answer null (Snail.MCP.Figma). A failure written through this type, which only a test does, passes
/// <c>default!</c> as its data, and it is left out as every field without a value is.
/// </para>
/// </remarks>
/// <typeparam name="T">What the tool answers.</typeparam>
/// <param name="IsOk">Whether the call did what was asked: <c>ok</c>.</param>
/// <param name="Data">What the tool answers, never null on a success; on a failure, what there is to report all the same.</param>
/// <param name="Error">Why the call did not go through; only on a failure.</param>
/// <param name="Hint">What to do about it; only on a failure.</param>
/// <param name="Type">The kind of failure, a word a program can branch on; only on a failure.</param>
public sealed record Envelope<T>(
    [property: JsonPropertyName("ok"), JsonPropertyOrder(0), Description("true: the call did what was asked. A refusal is an error result with ok false, error, hint and type in its text alone.")] bool IsOk,
    [property: JsonPropertyName("data"), JsonPropertyOrder(4), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Description("What the tool answers.")] T Data,
    [property: JsonPropertyName("error"), JsonPropertyOrder(1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Description("Why the call did not go through; present only on a refusal, which is an error result.")] string? Error = null,
    [property: JsonPropertyName("hint"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Description("What to do about a refusal; present only on one.")] string? Hint = null,
    [property: JsonPropertyName("type"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), Description("The kind of refusal, a word a program can branch on; present only on one.")] string? Type = null)
    where T : notnull;

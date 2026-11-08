namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>One parameter of a tool as a caller fills it: its name, its JSON type, whether it must be sent, what it means, and the fields of an object.</summary>
/// <remarks>
/// <para>
/// The fields come from the input schema two levels below the tool's parameter — its fields and theirs — through a
/// nullable object (<c>"type": ["object", "null"]</c>), an <c>anyOf</c> or <c>oneOf</c> of one object and null, and a
/// <c>$ref</c> into the same schema (<c>#/$defs/…</c>, <c>#/definitions/…</c>, or the pointer the MCP SDK writes for a
/// record that holds itself). An object shown without them told a model nothing of what to put in it, and Snail.MCP.Blender
/// takes 38 records; deeper than two levels, an answer read by models grows faster than what it tells.
/// </para>
/// <para>
/// An array of objects (<c>object[]</c>) carries the fields of each item. A field is required as its own schema says,
/// within its parent: a required field of an optional parameter is sent only when the parameter is.
/// </para>
/// </remarks>
/// <param name="Name">The name the call sends, as the tool's input schema spells it.</param>
/// <param name="Type">The JSON type of the value: <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c> or <c>object</c>; an array by its items, <c>string[]</c> or <c>integer[][]</c>, and <c>array</c> when they say nothing.</param>
/// <param name="IsRequired">Whether a call without it is refused.</param>
/// <param name="Description">What the model reads before filling it; null when the schema says nothing.</param>
/// <param name="Fields">The fields of an object, or of each item of an array of objects, in the order of its schema; null for any other value, and for an object two levels down or one whose schema names none.</param>
public sealed record ToolParameter(string Name, string Type, bool IsRequired, string? Description, IReadOnlyList<ToolParameter>? Fields = null);

/// <summary>A tool as discovery sees it: the name to call, what it does, the skill it arrives with, the command a program sends for it, and what it takes.</summary>
/// <remarks>
/// The parameters travel with a search so that finding a tool is enough to call it — directly, through <c>run_tools</c>,
/// or through a program by its command — without reading the tool list again.
/// </remarks>
/// <param name="Name">The tool's name as the protocol lists it.</param>
/// <param name="Description">The tool's description; empty when it has none.</param>
/// <param name="Skill">The skill that loads the tool, or null for a tool that is always listed.</param>
/// <param name="Command">What a sequence or a program of the server calls the tool, or null when it is not one of its commands.</param>
/// <param name="Parameters">The parameters, in the order of the tool's input schema.</param>
public sealed record ToolEntry(string Name, string Description, string? Skill, string? Command, IReadOnlyList<ToolParameter> Parameters);

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>One parameter of a tool as a caller fills it: its name, its JSON type, whether it must be sent, and what it means.</summary>
/// <param name="Name">The name the call sends, as the tool's input schema spells it.</param>
/// <param name="Type">The JSON type of the value: <c>string</c>, <c>integer</c>, <c>number</c>, <c>boolean</c> or <c>object</c>; an array by its items, <c>string[]</c> or <c>integer[][]</c>, and <c>array</c> when they say nothing.</param>
/// <param name="IsRequired">Whether a call without it is refused.</param>
/// <param name="Description">What the model reads before filling it; null when the schema says nothing.</param>
public sealed record ToolParameter(string Name, string Type, bool IsRequired, string? Description);

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

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>Puts the tools of a class into a skill: listed to a session only once it loads the skill, found by <c>find_tool</c> always.</summary>
/// <remarks>
/// Read from <c>McpServerTool.Metadata</c>, where the MCP SDK puts the attributes of a tool's class before those of its
/// method, so nothing looks the class up at run time. The reference of Snail.Sdk reads the same attribute — by its name
/// and its first argument — to put a skill's tools on a page of their own.
/// </remarks>
/// <param name="name">The skill's name, a lowercase word: <c>materials</c>, <c>animation</c>.</param>
/// <param name="description">What the skill's tools are for, in the words a model chooses a skill by.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SkillAttribute(string name, string description) : Attribute
{
    /// <summary>The skill's name, as <c>enable_skill</c> takes it.</summary>
    public string Name { get; } = name;

    /// <summary>What the skill's tools are for.</summary>
    public string Description { get; } = description;
}

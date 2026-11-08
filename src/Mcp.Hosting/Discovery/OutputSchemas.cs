using System.Text.Json;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>The output schemas of <c>find_tool</c> and <c>run_tools</c>, stated when the server states output schemas of its own.</summary>
/// <remarks>
/// <para>
/// A schema is stated once any other tool of the server states one: a client of such a server reads the structured
/// content against the schema, and Snail.MCP.FileSystem, which gives every tool one, had two tools without. A server that
/// states none keeps its tool list as it was, since every session reads the list whether or not it calls them.
/// </para>
/// <para>
/// The schemas leave descriptions out and name only what every answer carries as required: a failure carries no
/// structured content, so it is never read against them.
/// </para>
/// </remarks>
internal static class OutputSchemas
{
    private const string FindToolData = """
        {"type":"object","properties":{"task":{"type":["string","null"]},"isMatched":{"type":"boolean"},"note":{"type":"string"},
        "tools":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},"command":{"type":"string"},
        "description":{"type":"string"},"parameters":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string"},
        "type":{"type":"string"},"isRequired":{"type":"boolean"},"description":{"type":"string"},"fields":{"type":"array","items":{"type":"object",
        "required":["name","type","isRequired"]}}},"required":["name","type","isRequired"]}},
        "skill":{"type":"string"},"isLoaded":{"type":"boolean"}},"required":["name","description"]}},
        "enabledSkills":{"type":"array","items":{"type":"string"}}},"required":["task","isMatched","tools"]}
        """;

    private const string RunToolsData = """
        {"type":"object","properties":{"ran":{"type":"integer"},"steps":{"type":"array","items":{"type":"object","properties":{
        "tool":{"type":"string"},"ok":{"type":"boolean"},"data":{},"isOmitted":{"type":"boolean"},"error":{"type":"string"},
        "hint":{"type":"string"},"type":{"type":"string"}},"required":["tool","ok"]}},"omitted":{"type":"integer"},
        "note":{"type":"string"}},"required":["ran","steps"]}
        """;

    /// <summary>The schema of <c>find_tool</c>'s answer in the shape given, or null for <see cref="AnswerShape.None"/>.</summary>
    public static JsonElement? OfFindTool(AnswerShape shape) => Of(FindToolData, shape);

    /// <summary>The schema of <c>run_tools</c>' answer in the shape given, or null for <see cref="AnswerShape.None"/>.</summary>
    public static JsonElement? OfRunTools(AnswerShape shape) => Of(RunToolsData, shape);

    /// <summary>Whether a tool of the server other than discovery's own states an output schema.</summary>
    public static bool AreStated(IEnumerable<McpServerTool> tools) => tools.Any(tool => !IsDiscovery(tool) && tool.ProtocolTool.OutputSchema is not null);

    /// <summary>Whether the tool is one discovery adds or one that forwards to others, which states no schema of the server's.</summary>
    public static bool IsDiscovery(McpServerTool tool) =>
        tool.ProtocolTool.Name is FindTool.Name or RunTools.Name || ToolForwarding.IsForwarder(tool);

    private static JsonElement? Of(string data, AnswerShape shape) => shape switch
    {
        AnswerShape.Data => JsonElement.Parse(data),
        AnswerShape.Envelope => JsonElement.Parse($$"""{"type":"object","properties":{"ok":{"type":"boolean","const":true},"data":{{data}}},"required":["ok","data"]}"""),
        _ => null,
    };
}

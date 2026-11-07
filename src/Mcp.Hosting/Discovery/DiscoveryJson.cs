using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>One step of <c>run_tools</c>: the tool to call and the arguments to call it with.</summary>
internal sealed record ToolStep(
    [property: JsonPropertyName("tool"), Description(ToolDescriptions.StepTool)] string? Tool,
    [property: JsonPropertyName("arguments"), Description(ToolDescriptions.StepArguments)] JsonObject? Arguments = null);

/// <summary>The generated JSON of the tools discovery adds, so they bind their arguments in a native build.</summary>
[JsonSerializable(typeof(ToolStep[]))]
internal sealed partial class DiscoveryJson : JsonSerializerContext;

/// <summary>The options the tools discovery adds are registered with.</summary>
/// <remarks>Outside the context: a static member of the context's own built from <c>Default</c> may run before <c>Default</c> exists.</remarks>
internal static class DiscoveryWire
{
    /// <summary>The MCP SDK's options with the types of <see cref="DiscoveryJson"/> put first.</summary>
    public static JsonSerializerOptions Tools { get; } = ToolJson.Options(DiscoveryJson.Default);
}

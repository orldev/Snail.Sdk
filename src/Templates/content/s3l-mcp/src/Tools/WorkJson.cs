using System.Text.Json.Serialization;

namespace Snail.MCP.Template.Tools;

/// <summary>The generated JSON of what a job of this server ends with: a native build looks nothing up at run time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Counted))]
public sealed partial class WorkJson : JsonSerializerContext;

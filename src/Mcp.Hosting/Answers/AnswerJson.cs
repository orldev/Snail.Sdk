using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>The generated JSON of what answers are built from, so no server keeps a context of its own only for a JsonObject.</summary>
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class AnswerJson : JsonSerializerContext;

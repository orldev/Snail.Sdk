using System.Text.Json.Nodes;

namespace Snail.Sdk.Mcp.Http;

/// <summary>What a health check answers: whether the server can serve now, which decides 200 or 503, and the body that says more.</summary>
/// <param name="IsHealthy">Whether the server can serve now: 200 when it can, 503 when it cannot.</param>
/// <param name="Body">The JSON the check answers with — <c>{"status":"ok","blender":"down"}</c> — or none.</param>
/// <remarks>
/// The body is a <see cref="JsonNode"/>, which writes itself without reflection, so a native server answers with it as
/// it is; a server that holds a typed answer turns it into one with <c>JsonSerializer.SerializeToNode(value, typeInfo)</c>.
/// </remarks>
public sealed record HealthAnswer(bool IsHealthy, JsonNode? Body = null);

using System.Text.Json.Serialization;

namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>The generated JSON of a job's report: what its resource carries and a client reads, the same on a native build.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JobReport))]
internal sealed partial class JobJson : JsonSerializerContext;

using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Sdk.Benchmarks;

/// <summary>
/// The guard rules over the tool list of a large server: a hundred tools, each with three described parameters, one of
/// them an enum of four words — what every server's contract test runs on every list it receives.
/// </summary>
[MemoryDiagnoser]
public class ToolGuardsBenchmarks
{
    private const int Tools = 100;

    private Tool[] _tools = [];

    [GlobalSetup]
    public void Setup() => _tools = [.. Enumerable.Range(0, Tools).Select(Described)];

    [Benchmark]
    public int Problems_OfAHundredTools() => ToolGuards.Problems(_tools).Count;

    private static Tool Described(int index)
    {
        var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "The file to read, from the repository root." },
                "scope": { "type": "string", "enum": ["mine", "everything", "orphans", "kept"], "description": "What to look at: mine, everything, orphans or kept." },
                "limit": { "type": "integer", "description": "How many entries to answer with at most." }
              }
            }
            """).RootElement;

        return new Tool
        {
            Name = $"bench_tool_{index}",
            Description = $"Benchmark tool {index}: reads a file and answers with its entries.",
            InputSchema = schema,
            Annotations = new ToolAnnotations { ReadOnlyHint = true },
        };
    }
}

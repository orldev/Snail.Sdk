using BenchmarkDotNet.Attributes;
using ModelContextProtocol.Protocol;
using Snail.MCP.Template.Tools;

namespace Snail.MCP.Template.Benchmarks;

/// <summary>The answer of a tool, measured; the shared perf workflow compares its allocations with the baseline.</summary>
[MemoryDiagnoser]
public class DiagnosticsBenchmarks
{
    /// <summary>One diagnosis.</summary>
    [Benchmark]
    public CallToolResult Diagnose() => DiagnosticsTools.Diagnose();
}

using BenchmarkDotNet.Attributes;

namespace Snail.Toolkit.Template.Benchmarks;

/// <summary>The hot path of the library, measured; the shared perf workflow compares its allocations with the baseline.</summary>
[MemoryDiagnoser]
public class GreetingBenchmarks
{
    /// <summary>A greeting for one name.</summary>
    [Benchmark]
    public string For() => Greeting.For("world");
}

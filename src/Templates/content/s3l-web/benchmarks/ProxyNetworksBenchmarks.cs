using System.Net;
using BenchmarkDotNet.Attributes;
using Snail.Web.Template.Proxy;

namespace Snail.Web.Template.Benchmarks;

/// <summary>The proxy's networks read from configuration, measured; the shared perf workflow compares its allocations with the baseline.</summary>
[MemoryDiagnoser]
public class ProxyNetworksBenchmarks
{
    private readonly string[] _networks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

    /// <summary>The private networks a Docker host gives its containers.</summary>
    [Benchmark]
    public IReadOnlyList<IPNetwork> Parse() => ProxyNetworks.Parse(_networks);
}

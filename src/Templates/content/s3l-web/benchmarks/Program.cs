using BenchmarkDotNet.Running;
using Snail.Web.Template.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(ProxyNetworksBenchmarks).Assembly).Run(args);

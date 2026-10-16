using BenchmarkDotNet.Running;
using Snail.Toolkit.Template.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(GreetingBenchmarks).Assembly).Run(args);

using BenchmarkDotNet.Running;
using Snail.MCP.Template.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(DiagnosticsBenchmarks).Assembly).Run(args);

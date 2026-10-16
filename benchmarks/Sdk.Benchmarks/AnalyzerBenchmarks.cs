using System.Collections.Immutable;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Snail.Sdk.Analyzers;

namespace Snail.Sdk.Sdk.Benchmarks;

/// <summary>
/// The five analyzers over the SDK's own Conformance and Docs sources, as every build of a fleet project runs them; the
/// compiler's own diagnostics of the same compilation are the baseline the analyzers' cost is read against. Each
/// measurement starts from a clone of the compilation, since a compilation keeps the diagnostics it has computed once
/// and the second call would measure a cache.
/// </summary>
[MemoryDiagnoser]
public class AnalyzerBenchmarks
{
    private static readonly ImmutableArray<DiagnosticAnalyzer> FiveAnalyzers = [new QueryShape(), new ReturnShape(), new SourceText(), new Naming(), new Simplification()];

    private CSharpCompilation _compilation = null!;

    [GlobalSetup]
    public void Setup()
    {
        var root = RepositoryRoot();
        var conformance = Directory.EnumerateFiles(Path.Combine(root, "src", "Conformance"), "*.cs");
        var docs = Directory.EnumerateFiles(Path.Combine(root, "src", "Docs"), "*.cs");
        var trees = conformance.Concat(docs).Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));
        var platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var references = platform.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

        _compilation = CSharpCompilation.Create("Sample", trees, references, options);
    }

    [Benchmark(Baseline = true)]
    public int Diagnostics_OfTheCompilerAlone() => _compilation.Clone().GetDiagnostics().Length;

    [Benchmark]
    public async Task<int> Diagnostics_WithTheFiveAnalyzers()
    {
        var fresh = _compilation.Clone();
        var compiler = fresh.GetDiagnostics().Length;
        var analyzers = await fresh.WithAnalyzers(FiveAnalyzers).GetAnalyzerDiagnosticsAsync();

        return compiler + analyzers.Length;
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Snail.Sdk.slnx")))
            folder = folder.Parent;

        return folder?.FullName ?? throw new InvalidOperationException("Snail.Sdk.slnx was not found above the benchmarks.");
    }
}

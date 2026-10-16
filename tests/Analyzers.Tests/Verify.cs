using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Snail.Sdk.Analyzers.Tests;

/// <summary>Runs one analyzer, or one analyzer with its fix, over C# written the way the fleet writes it.</summary>
/// <remarks>The latest language version, so collection expressions and primary constructors parse as they do in the fleet.</remarks>
public static class Verify<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
{
    /// <summary>The analyzer reports exactly the diagnostics marked in the source, with a PublicAPI.Shipped.txt when one is given.</summary>
    public static Task AnalyzerAsync(string source, string? projectKind = null, string? shippedApi = null)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier> { TestCode = source, ReferenceAssemblies = ReferenceAssemblies.Net.Net80 };

        Configure(test.SolutionTransforms, test.TestState, projectKind);

        if (shippedApi is not null)
            test.TestState.AdditionalFiles.Add(("/PublicAPI.Shipped.txt", shippedApi));

        return test.RunAsync();
    }

    /// <summary>The fix turns the marked source into the expected one.</summary>
    public static Task FixAsync<TFix>(string source, string expected) where TFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TFix, DefaultVerifier> { TestCode = source, FixedCode = expected, ReferenceAssemblies = ReferenceAssemblies.Net.Net80 };

        Configure(test.SolutionTransforms, test.TestState, projectKind: null);

        return test.RunAsync();
    }

    private static void Configure(List<Func<Solution, ProjectId, Solution>> transforms, SolutionState state, string? projectKind)
    {
        transforms.Add((solution, project) => solution.WithProjectParseOptions(project, new CSharpParseOptions(LanguageVersion.Latest)));

        if (projectKind is not null)
            state.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\nbuild_property.SnailProjectKind = {projectKind}\n"));
    }
}

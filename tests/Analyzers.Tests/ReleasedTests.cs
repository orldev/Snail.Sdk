using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Snail.Sdk.Analyzers.Tests;

public sealed class ReleasedTests
{
    /// <summary>A released name stays as it shipped; a new method is still held to the suffix.</summary>
    [Fact]
    public Task AsyncSuffix_OnAShippedName_IsSuppressed()
    {
        var test = new SuppressorTest
        {
            TestCode = """
                using System.Threading.Tasks;

                namespace Library;

                public sealed class Tokens
                {
                    public Task<bool> Validate(string token) => Task.FromResult(token.Length > 0);
                    public Task<bool> Refresh(string token) => Task.FromResult(token.Length > 0);
                }
                """,
        };

        test.TestState.AdditionalFiles.Add(("/PublicAPI.Shipped.txt", """
            #nullable enable
            Library.Tokens
            Library.Tokens.Tokens() -> void
            Library.Tokens.Validate(string! token) -> System.Threading.Tasks.Task<bool>!
            """));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithSpan(7, 23, 7, 31).WithIsSuppressed(true));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithSpan(8, 23, 8, 30).WithIsSuppressed(false));

        return test.RunAsync();
    }

    /// <summary>A generic method is found among the shipped names, though its line spells its type arguments.</summary>
    [Fact]
    public Task AsyncSuffix_OnAShippedGenericName_IsSuppressed()
    {
        var test = new SuppressorTest
        {
            TestCode = """
                using System.Collections.Generic;

                namespace Library;

                public static class Reads<TKey>
                {
                    public static async IAsyncEnumerable<TOutput> AggregateAsAsyncEnumerable<TInput, TOutput>(TInput input) { yield break; }
                }
                """,
        };

        test.TestState.AdditionalFiles.Add(("/PublicAPI.Shipped.txt", """
            #nullable enable
            Library.Reads<TKey>
            static Library.Reads<TKey>.AggregateAsAsyncEnumerable<TInput, TOutput>(TInput input) -> System.Collections.Generic.IAsyncEnumerable<TOutput>!
            """));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithSpan(7, 51, 7, 77).WithIsSuppressed(true));

        return test.RunAsync();
    }

    /// <summary>A parameter, a local or a local function that shares a shipped method's name was never released.</summary>
    [Fact]
    public Task AsyncSuffix_OnANameInsideAShippedType_IsNotSuppressed()
    {
        var test = new SuppressorTest
        {
            TestCode = """
                using System.Threading.Tasks;

                namespace Library;

                public sealed class Tokens
                {
                    public Task<bool> {|#0:Validate|}(string token) => Task.FromResult(token.Length > 0);

                    public int {|#4:Measure|}(string {|#1:Validate|}) => Validate.Length;

                    public int {|#5:Count|}()
                    {
                        var {|#2:Validate|} = 1;

                        return Validate;
                    }

                    public bool {|#6:Check|}()
                    {
                        return Validate();

                        static bool {|#3:Validate|}() => true;
                    }
                }
                """,
        };

        test.TestState.AdditionalFiles.Add(("/PublicAPI.Shipped.txt", """
            #nullable enable
            Library.Tokens
            Library.Tokens.Tokens() -> void
            Library.Tokens.Validate(string! token) -> System.Threading.Tasks.Task<bool>!
            """));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithLocation(0).WithIsSuppressed(true));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithLocation(1).WithIsSuppressed(false));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithLocation(2).WithIsSuppressed(false));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithLocation(3).WithIsSuppressed(false));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Oblivious).WithLocation(4).WithArguments("Measure"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Oblivious).WithLocation(5).WithArguments("Count"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Oblivious).WithLocation(6).WithArguments("Check"));

        return test.RunAsync();
    }

    /// <summary>A shipped line names what it declares: a method is not the property the line released under that name.</summary>
    [Fact]
    public Task AsyncSuffix_OnAMethodShippedAsAProperty_IsNotSuppressed()
    {
        var test = new SuppressorTest
        {
            TestCode = """
                using System.Threading.Tasks;

                namespace Library;

                public sealed class Tokens
                {
                    public Task<bool> {|#0:Pending|}() => Task.FromResult(true);
                }
                """,
        };

        test.TestState.AdditionalFiles.Add(("/PublicAPI.Shipped.txt", """
            #nullable enable
            Library.Tokens
            Library.Tokens.Pending.get -> bool
            """));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Suffix).WithLocation(0).WithIsSuppressed(false));

        return test.RunAsync();
    }

    /// <summary>The code Razor generates is not the library's to annotate; the same finding in written code stands.</summary>
    [Fact]
    public Task ObliviousType_InRazorOutput_IsSuppressed()
    {
        var test = new SuppressorTest();

        test.TestState.Sources.Add(("/Components_Field_razor.g.cs", "public sealed class Field { public void BuildRenderTree(object builder) { } }"));
        test.TestState.Sources.Add(("/Written.cs", "public sealed class Written { public void Render(object builder) { } }"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Oblivious).WithSpan("/Components_Field_razor.g.cs", 1, 41, 1, 56).WithArguments("BuildRenderTree").WithIsSuppressed(true));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(StandIn.Oblivious).WithSpan("/Written.cs", 1, 43, 1, 49).WithArguments("Render").WithIsSuppressed(false));

        return test.RunAsync();
    }

    private sealed class SuppressorTest : CSharpAnalyzerTest<StandIn, DefaultVerifier>
    {
        public SuppressorTest()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
            SolutionTransforms.Add((solution, project) => solution.WithProjectParseOptions(project, new CSharpParseOptions(LanguageVersion.Latest)));
        }

        protected override IEnumerable<DiagnosticAnalyzer> GetDiagnosticAnalyzers() => [new StandIn(), new Released()];
    }

    /// <summary>
    /// Stands in for the analyzers the suppressor answers: IDE1006 on an asynchronous method, on a local function and on a
    /// PascalCase local or parameter; RS0041 on every other method.
    /// </summary>
    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1036", Justification = "A stand-in that only the test harness loads, never a compiler.")]
    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1038", Justification = "A stand-in that only the test harness loads, never a compiler.")]
    [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1041", Justification = "A stand-in that only the test harness loads, never a compiler.")]
    [SuppressMessage("MicrosoftCodeAnalysisReleaseTracking", "RS2008", Justification = "The ids are borrowed from the analyzers the suppressor answers; nothing is released.")]
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    private sealed class StandIn : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor Suffix = new("IDE1006", "Naming rule violation", "Missing suffix: 'Async'", "Style", DiagnosticSeverity.Warning, true);

        public static readonly DiagnosticDescriptor Oblivious = new("RS0041", "Oblivious type", "'{0}' uses an oblivious type", "ApiDesign", DiagnosticSeverity.Warning, true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Suffix, Oblivious];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(Report, SymbolKind.Method);
            context.RegisterSyntaxNodeAction(ReportLocal, SyntaxKind.VariableDeclarator, SyntaxKind.Parameter, SyntaxKind.LocalFunctionStatement);
        }

        private static void ReportLocal(SyntaxNodeAnalysisContext context)
        {
            var identifier = context.Node switch
            {
                VariableDeclaratorSyntax variable => variable.Identifier,
                ParameterSyntax parameter => parameter.Identifier,
                LocalFunctionStatementSyntax function => function.Identifier,
                _ => default
            };

            if (context.Node is LocalFunctionStatementSyntax || identifier.ValueText is [>= 'A' and <= 'Z', ..])
                context.ReportDiagnostic(Diagnostic.Create(Suffix, identifier.GetLocation()));
        }

        private static void Report(SymbolAnalysisContext context)
        {
            var method = (IMethodSymbol)context.Symbol;

            if (method.MethodKind != MethodKind.Ordinary)
                return;

            var rule = method.ReturnType.Name is "Task" or "IAsyncEnumerable" ? Suffix : Oblivious;

            context.ReportDiagnostic(Diagnostic.Create(rule, method.Locations[0], method.Name));
        }
    }
}

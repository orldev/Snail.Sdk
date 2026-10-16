using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>S3L0001: a LINQ chain past four operators, or a lambda nested two deep, is unpacked into named steps.</summary>
/// <remarks>
/// An operator is a call named like one of LINQ's on a receiver that is a sequence — an <c>IEnumerable&lt;T&gt;</c>,
/// <c>IQueryable&lt;T&gt;</c>, <c>IAsyncEnumerable&lt;T&gt;</c> or <c>IObservable&lt;T&gt;</c>: a chain over an observable reads the
/// same as one over an enumerable, while a builder's <c>Append</c> chain is no query. The names are counted first, and the
/// semantic model is asked about the receivers only of a chain long enough to be reported.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryShape : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> Operators = ImmutableHashSet.Create(StringComparer.Ordinal,
        "Select", "SelectMany", "Where", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "GroupBy", "Join",
        "GroupJoin", "Distinct", "DistinctBy", "Union", "Intersect", "Except", "Concat", "Zip", "Skip", "SkipWhile", "Take",
        "TakeWhile", "Any", "All", "Count", "LongCount", "Sum", "Min", "Max", "MinBy", "MaxBy", "Average", "Aggregate", "First",
        "FirstOrDefault", "Last", "LastOrDefault", "Single", "SingleOrDefault", "ElementAt", "ToList", "ToArray",
        "ToDictionary", "ToHashSet", "ToLookup", "Cast", "OfType", "Reverse", "Chunk", "CountBy", "AggregateBy", "Index",
        "Prepend", "Append", "DefaultIfEmpty", "SequenceEqual", "Contains", "AsEnumerable", "ToFrozenSet", "ToFrozenDictionary");

    private static readonly ImmutableHashSet<string> Sequences = ImmutableHashSet.Create(StringComparer.Ordinal,
        "System.Collections.Generic.IEnumerable`1", "System.Linq.IQueryable`1", "System.Collections.Generic.IAsyncEnumerable`1",
        "System.IObservable`1");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.UnpackTheQuery];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeChain, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression);
    }

    private static void AnalyzeChain(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax })
            return;

        var named = Links(invocation).Where(link => Operators.Contains(link.Name.Identifier.Text)).ToList();

        if (named.Count <= Rules.MostQueryOperators)
            return;

        var operators = named.Count(link => IsSequence(context.SemanticModel.GetTypeInfo(link.Expression, context.CancellationToken).Type));

        if (operators > Rules.MostQueryOperators)
            context.ReportDiagnostic(Diagnostic.Create(Rules.UnpackTheQuery, invocation.GetLocation(), $"{operators} operators in one chain"));
    }

    private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        var enclosing = context.Node.Ancestors().OfType<LambdaExpressionSyntax>().Count();

        if (enclosing == Rules.DeepestLambda)
            context.ReportDiagnostic(Diagnostic.Create(Rules.UnpackTheQuery, context.Node.GetLocation(), $"a lambda nested {enclosing} deep"));
    }

    private static IEnumerable<MemberAccessExpressionSyntax> Links(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation;

        while (current is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access })
        {
            yield return access;

            current = access.Expression;
        }
    }

    /// <summary>Whether the receiver is one of the sequences LINQ queries, or implements one.</summary>
    private static bool IsSequence(ITypeSymbol? receiver) =>
        receiver is not null && (IsSequenceContract(receiver) || receiver.AllInterfaces.Any(IsSequenceContract));

    private static bool IsSequenceContract(ITypeSymbol type) =>
        type.OriginalDefinition is INamedTypeSymbol { Arity: 1 } contract
        && Sequences.Contains($"{contract.ContainingNamespace.ToDisplayString()}.{contract.MetadataName}");
}

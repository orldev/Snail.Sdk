using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>S3L0002 and S3L0003: how a method gives its answer — not hidden in a large construction, and not without air.</summary>
/// <remarks>
/// An expression-bodied member that only builds an object (<c>Theme() =&gt; new(…)</c>) is left alone: the member's name
/// already names what it builds, and a variable would only repeat it.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReturnShape : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.CreateThenReturn, Rules.LetItBreathe];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeReturned, SyntaxKind.ReturnStatement, SyntaxKind.YieldReturnStatement);
        context.RegisterSyntaxNodeAction(AnalyzeBody, SyntaxKind.Block);
    }

    /// <summary>The object a return, a yield return or an expression body builds in place, when it builds one.</summary>
    public static ExpressionSyntax? Built(SyntaxNode node)
    {
        var returned = node switch
        {
            ReturnStatementSyntax ret => ret.Expression,
            YieldStatementSyntax yield => yield.Expression,
            ArrowExpressionClauseSyntax arrow => arrow.Expression,
            _ => null
        };

        return returned is BaseObjectCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax or CollectionExpressionSyntax ? returned : null;
    }

    /// <summary>How many lines a node spans.</summary>
    public static int Lines(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();

        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static void AnalyzeReturned(SyntaxNodeAnalysisContext context)
    {
        if (Built(context.Node) is not { } built)
            return;

        var lines = Lines(built);

        if (lines > Rules.LongestReturnedObject)
            context.ReportDiagnostic(Diagnostic.Create(Rules.CreateThenReturn, built.GetLocation(), lines));
    }

    private static void AnalyzeBody(SyntaxNodeAnalysisContext context)
    {
        var body = (BlockSyntax)context.Node;

        if (body.Parent is not (BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax))
            return;

        if (body.Statements.Count < Rules.StatementsThatBreathe || body.Statements.Last() is not ReturnStatementSyntax final)
            return;

        var previousEnd = body.Statements[body.Statements.Count - 2].GetLocation().GetLineSpan().EndLinePosition.Line;
        var finalStart = final.GetLocation().GetLineSpan().StartLinePosition.Line;

        if (finalStart - previousEnd < 2)
            context.ReportDiagnostic(Diagnostic.Create(Rules.LetItBreathe, final.ReturnKeyword.GetLocation()));
    }
}

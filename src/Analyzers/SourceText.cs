using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>S3L0004 and S3L0005: what the source says in words — no comments inside members, and English throughout.</summary>
/// <remarks>
/// String literals are held to English only in the code that ships: a test's or a benchmark's text is data, often the
/// very non-English input the code must handle (Snail.Sdk passes the project's kind as <c>build_property.SnailProjectKind</c>).
/// A string an LLM must read verbatim lives in a type or namespace named for prompts, and is exempt too.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SourceText : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.NoInlineComments, Rules.EnglishOnly];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(Analyze);
    }

    private static void Analyze(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        var isData = options.TryGetValue("build_property.SnailProjectKind", out var kind) && kind is "Tests" or "Benchmarks";

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if (!IsComment(trivia))
                continue;

            if (IsInsideBody(trivia))
                context.ReportDiagnostic(Diagnostic.Create(Rules.NoInlineComments, trivia.GetLocation()));

            if (HasCyrillic(trivia.ToFullString()))
                context.ReportDiagnostic(Diagnostic.Create(Rules.EnglishOnly, trivia.GetLocation(), "comment"));
        }

        foreach (var token in root.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.IdentifierToken) && HasCyrillic(token.ValueText))
                context.ReportDiagnostic(Diagnostic.Create(Rules.EnglishOnly, token.GetLocation(), "name"));
            else if (!isData && IsText(token) && HasCyrillic(token.ValueText) && !IsPrompt(token))
                context.ReportDiagnostic(Diagnostic.Create(Rules.EnglishOnly, token.GetLocation(), "text"));
        }
    }

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private static bool IsInsideBody(SyntaxTrivia trivia)
    {
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            return false;

        var bodies = trivia.Token.Parent?.AncestorsAndSelf().Where(IsBody) ?? [];

        return bodies.Any(body => trivia.SpanStart > body.SpanStart && trivia.Span.End < body.Span.End);
    }

    private static bool IsBody(SyntaxNode node) =>
        node is BlockSyntax { Parent: BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax }
            or ArrowExpressionClauseSyntax;

    private static bool IsText(SyntaxToken token) =>
        token.IsKind(SyntaxKind.StringLiteralToken) || token.IsKind(SyntaxKind.InterpolatedStringTextToken)
        || token.IsKind(SyntaxKind.SingleLineRawStringLiteralToken) || token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken)
        || token.IsKind(SyntaxKind.Utf8StringLiteralToken) || token.IsKind(SyntaxKind.CharacterLiteralToken);

    private static bool IsPrompt(SyntaxToken token) =>
        token.Parent?.Ancestors().Any(node => node switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.Text.Contains("Prompt"),
            BaseNamespaceDeclarationSyntax space => space.Name.ToString().Contains("Prompt"),
            _ => false
        }) ?? false;

    private static bool HasCyrillic(string text) => text.Any(letter => letter is >= 'Ѐ' and <= 'ӿ');
}

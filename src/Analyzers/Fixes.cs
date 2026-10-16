using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Snail.Sdk.Analyzers;

/// <summary>S3L0009: removes the <c>this.</c> nothing needs.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveThis)), Shared]
public sealed class RemoveThis : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.NoThis.Id];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root?.FindNode(context.Span, getInnermostNodeForTie: true).FirstAncestorOrSelf<MemberAccessExpressionSyntax>() is not { } access)
            return;

        var fixedName = access.Name.WithTriviaFrom(access);

        context.RegisterCodeFix(
            CodeAction.Create("Remove 'this.'", _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(access, fixedName))), nameof(RemoveThis)),
            context.Diagnostics);
    }
}

/// <summary>S3L0010: writes the keyword in place of the framework type's name.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseKeyword)), Shared]
public sealed class UseKeyword : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.KeywordTypes.Id];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
            return;

        var spelling = root.FindNode(context.Span, getInnermostNodeForTie: false);

        if (model.GetSymbolInfo(spelling, context.CancellationToken).Symbol is not ITypeSymbol type
            || !Simplification.Keywords.TryGetValue(type.SpecialType, out var keyword))
            return;

        var keywordType = SyntaxFactory.ParseTypeName(keyword).WithTriviaFrom(spelling);

        context.RegisterCodeFix(
            CodeAction.Create($"Use '{keyword}'", _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(spelling, keywordType))), nameof(UseKeyword)),
            context.Diagnostics);
    }
}

/// <summary>S3L0003: puts a blank line before the final return.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BreatheBeforeReturn)), Shared]
public sealed class BreatheBeforeReturn : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.LetItBreathe.Id];

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root?.FindToken(context.Span.Start).Parent?.FirstAncestorOrSelf<ReturnStatementSyntax>() is not { } final)
            return;

        var breathing = final.WithLeadingTrivia(final.GetLeadingTrivia().Insert(0, LineBreaks.Of(root)));

        context.RegisterCodeFix(
            CodeAction.Create("Put a blank line before the return", _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(final, breathing))), nameof(BreatheBeforeReturn)),
            context.Diagnostics);
    }
}

/// <summary>S3L0002: builds the large object into a named variable, then returns the variable.</summary>
/// <remarks>Offered for a return or a yield return inside a block; an expression body would first have to become a block.</remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CreateThenReturn)), Shared]
public sealed class CreateThenReturn : CodeFixProvider
{
    /// <inheritdoc />
    public override ImmutableArray<string> FixableDiagnosticIds => [Rules.CreateThenReturn.Id];

    /// <inheritdoc />
    public override FixAllProvider? GetFixAllProvider() => null;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null || model is null)
            return;

        var statement = root.FindNode(context.Span).FirstAncestorOrSelf<StatementSyntax>();

        if (statement is not (ReturnStatementSyntax or YieldStatementSyntax) || statement.Parent is not BlockSyntax block || ReturnShape.Built(statement) is not { } built)
            return;

        if (DeclaredType(built, model, statement.SpanStart) is not { } declaredType)
            return;

        var name = FreeName(model, statement.SpanStart, Named(built, model));
        var endOfLine = LineBreaks.Of(root);

        context.RegisterCodeFix(
            CodeAction.Create($"Create '{name}', then return it", _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(block, Split(block, statement, built, declaredType, name, endOfLine)))), nameof(CreateThenReturn)),
            context.Diagnostics);
    }

    /// <summary>
    /// <c>var</c> for an object that names its type; the type it converts to for a collection expression or a target-typed
    /// <c>new(…)</c>, which have none of their own; nothing when that type cannot be written.
    /// </summary>
    private static TypeSyntax? DeclaredType(ExpressionSyntax built, SemanticModel model, int position)
    {
        if (built is not (CollectionExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
            return SyntaxFactory.IdentifierName("var");

        if (model.GetTypeInfo(built).ConvertedType is not { } converted || !CanBeWritten(converted))
            return null;

        return SyntaxFactory.ParseTypeName(converted.ToMinimalDisplayString(model, position));
    }

    private static bool CanBeWritten(ITypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Error } or { IsAnonymousType: true } => false,
        IArrayTypeSymbol array => CanBeWritten(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.All(CanBeWritten),
        _ => true
    };

    private static BlockSyntax Split(BlockSyntax block, StatementSyntax statement, ExpressionSyntax built, TypeSyntax declaredType, string name, SyntaxTrivia endOfLine)
    {
        var declaration = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    declaredType.WithTrailingTrivia(SyntaxFactory.Space),
                    [SyntaxFactory.VariableDeclarator(name).WithInitializer(SyntaxFactory.EqualsValueClause(built.WithoutTrivia()))]))
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithTrailingTrivia(endOfLine);

        var answer = statement.ReplaceNode(built, SyntaxFactory.IdentifierName(name).WithTriviaFrom(built))
            .WithLeadingTrivia(statement.GetLeadingTrivia().Insert(0, endOfLine));

        var index = block.Statements.IndexOf(statement);

        return block.WithStatements(block.Statements.RemoveAt(index).InsertRange(index, [declaration, answer]));
    }

    private static string Named(ExpressionSyntax built, SemanticModel model)
    {
        var typeName = model.GetTypeInfo(built).Type?.Name;

        return string.IsNullOrEmpty(typeName) || built is AnonymousObjectCreationExpressionSyntax
            ? "result"
            : $"{char.ToLowerInvariant(typeName![0])}{typeName.Substring(1)}";
    }

    private static string FreeName(SemanticModel model, int position, string wanted)
    {
        var name = wanted;

        for (var suffix = 2; !model.LookupSymbols(position, name: name).IsEmpty; suffix++)
            name = $"{wanted}{suffix}";

        return name;
    }
}

/// <summary>The line break a document already uses, so that a fix writes CRLF into a CRLF file and LF into an LF one.</summary>
internal static class LineBreaks
{
    /// <summary>The document's first line break, or LF when it has none.</summary>
    public static SyntaxTrivia Of(SyntaxNode root)
    {
        var first = root.DescendantTrivia().FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia));

        return first.IsKind(SyntaxKind.EndOfLineTrivia) ? first : SyntaxFactory.EndOfLine("\n");
    }
}

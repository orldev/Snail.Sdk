using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>S3L0009 and S3L0010: the two simplifications the guidelines ask for and the build does not run.</summary>
/// <remarks>
/// IDE0003 (no <c>this.</c>) and IDE0049 (keywords for framework types) report only in an editor: a command-line build
/// runs neither, even when a global config names them as errors. These carry them into the build. A type name is
/// put to the semantic model only when its last identifier could be a framework type: one of the names in
/// <see cref="Keywords"/> or an alias the compilation declares.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Simplification : DiagnosticAnalyzer
{
    /// <summary>The keyword for each framework type that has one.</summary>
    public static readonly ImmutableDictionary<SpecialType, string> Keywords = new Dictionary<SpecialType, string>
    {
        [SpecialType.System_Boolean] = "bool",
        [SpecialType.System_Byte] = "byte",
        [SpecialType.System_SByte] = "sbyte",
        [SpecialType.System_Char] = "char",
        [SpecialType.System_Decimal] = "decimal",
        [SpecialType.System_Double] = "double",
        [SpecialType.System_Single] = "float",
        [SpecialType.System_Int16] = "short",
        [SpecialType.System_UInt16] = "ushort",
        [SpecialType.System_Int32] = "int",
        [SpecialType.System_UInt32] = "uint",
        [SpecialType.System_Int64] = "long",
        [SpecialType.System_UInt64] = "ulong",
        [SpecialType.System_Object] = "object",
        [SpecialType.System_String] = "string"
    }.ToImmutableDictionary();

    /// <summary>The framework names of the types in <see cref="Keywords"/>: Int32, String and the rest.</summary>
    private static readonly ImmutableHashSet<string> FrameworkNames =
        ImmutableHashSet.CreateRange(StringComparer.Ordinal, Keywords.Keys.Select(type => type.ToString().Substring("System_".Length)));

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.NoThis, Rules.KeywordTypes];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeThis, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterCompilationStartAction(start =>
        {
            var spellings = FrameworkNames.Union(Aliases(start.Compilation, start.CancellationToken));

            start.RegisterSyntaxNodeAction(name => AnalyzeTypeName(name, spellings), SyntaxKind.IdentifierName, SyntaxKind.QualifiedName, SyntaxKind.AliasQualifiedName);
        });
    }

    /// <summary>Whether a name reached through <c>this.</c> means the same member without it.</summary>
    public static bool IsRedundantThis(MemberAccessExpressionSyntax access, SemanticModel model, CancellationToken cancellation)
    {
        if (access.Expression is not ThisExpressionSyntax || access.Name is not IdentifierNameSyntax name)
            return false;

        if (model.GetSymbolInfo(access, cancellation).Symbol is not { } member || member is IMethodSymbol { ReducedFrom: not null })
            return false;

        var visible = model.LookupSymbols(access.SpanStart, name: name.Identifier.ValueText);

        return visible.Length > 0
            && visible.All(symbol => symbol is not (ILocalSymbol or IParameterSymbol or IRangeVariableSymbol))
            && visible.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, member.OriginalDefinition));
    }

    private static void AnalyzeThis(SyntaxNodeAnalysisContext context)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;

        if (IsRedundantThis(access, context.SemanticModel, context.CancellationToken))
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoThis, access.Expression.GetLocation(), access.Name.Identifier.ValueText));
    }

    /// <summary>The whole spelling of a framework type a name ends: <c>String</c>, <c>System.String</c> as a qualified name, or as a member access.</summary>
    public static SyntaxNode Spelling(NameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;

    private static void AnalyzeTypeName(SyntaxNodeAnalysisContext context, ImmutableHashSet<string> spellings)
    {
        var name = (NameSyntax)context.Node;

        if (!spellings.Contains(LastIdentifier(name)) || name is IdentifierNameSyntax { IsVar: true } || name.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax || IsOutsideCode(name))
            return;

        if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is not ITypeSymbol type)
            return;

        if (!Keywords.TryGetValue(type.SpecialType, out var keyword))
            return;

        var spelling = Spelling(name);

        context.ReportDiagnostic(Diagnostic.Create(Rules.KeywordTypes, spelling.GetLocation(), keyword, spelling.ToString()));
    }

    private static string LastIdentifier(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax aliased => aliased.Name.Identifier.ValueText,
        SimpleNameSyntax simple => simple.Identifier.ValueText,
        _ => string.Empty
    };

    /// <summary>The names of the using aliases the compilation declares, in a file or globally, whatever they stand for.</summary>
    private static IEnumerable<string> Aliases(Compilation compilation, CancellationToken cancellation) =>
        compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot(cancellation).DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
            .OfType<UsingDirectiveSyntax>()
            .Where(directive => directive.Alias is not null)
            .Select(directive => directive.Alias!.Name.Identifier.ValueText);

    private static bool IsOutsideCode(SyntaxNode name) =>
        name.Ancestors().Any(node => node is UsingDirectiveSyntax or AttributeArgumentSyntax
            || node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } });
}

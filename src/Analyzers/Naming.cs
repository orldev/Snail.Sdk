using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>S3L0006, S3L0007 and S3L0008: names that say what a thing is, what it holds, and what a boolean asks.</summary>
/// <remarks>
/// A member that overrides or implements another keeps the name it was given there: the rule is for names chosen here.
/// A type in a name counts only when the declared type agrees with it, so a playlist that is a list is flagged and a
/// word that merely ends the same way is not.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Naming : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<string> PatternSuffixes = ["Service", "Manager", "Helper", "Helpers", "Processor", "Data", "Util", "Utils", "Utility", "Utilities"];

    private static readonly ImmutableHashSet<string> PatternFolders = ImmutableHashSet.Create(StringComparer.Ordinal, "Helpers", "Utils", "Managers", "Common", "Shared");

    private static readonly ImmutableArray<string> Questions =
    [
        "Is", "Has", "Have", "Can", "Could", "Should", "Was", "Were", "Will", "Would", "Does", "Do", "Did", "Are", "Allows",
        "Allow", "Needs", "Need", "Must", "May", "Try", "Contains", "Equals", "Exists", "Matches", "Supports", "Accepts",
        "Requires", "Includes", "Expects", "Knows", "Holds", "Owns", "Covers", "Fits", "Wants", "Shows", "Uses", "Keeps",
        "Starts", "Ends", "Remains", "Returns", "Throws", "Succeeded", "Failed", "Enabled", "Disabled", "Any", "All", "None",
        "Only", "Always", "Never", "Fails", "Succeeds", "Permits", "Lacks", "Misses", "Belongs", "Equal", "Same", "Overlaps", "Intersects", "Moves"
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rules.NoPatternNames, Rules.NoTypesInNames, Rules.BooleansAsk];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var shipped = ShippedApi.Of(start.Options, start.CancellationToken);

            start.RegisterSymbolAction(symbol => AnalyzeType(symbol, shipped), SymbolKind.NamedType);
            start.RegisterSymbolAction(symbol => AnalyzeMember(symbol, shipped), SymbolKind.Property, SymbolKind.Method, SymbolKind.Field);
        });
        context.RegisterSyntaxNodeAction(AnalyzeNamespace, SyntaxKind.NamespaceDeclaration, SyntaxKind.FileScopedNamespaceDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeLocal, SyntaxKind.VariableDeclarator, SyntaxKind.Parameter, SyntaxKind.ForEachStatement);
    }

    private static void AnalyzeType(SymbolAnalysisContext context, ShippedApi shipped)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.IsImplicitlyDeclared || shipped.Contains(type) || type.Locations.FirstOrDefault() is not { IsInSource: true } location)
            return;

        var suffix = PatternSuffixes.FirstOrDefault(pattern => type.Name.Length > pattern.Length && type.Name.EndsWith(pattern, StringComparison.Ordinal));

        if (suffix is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoPatternNames, location, type.Name, suffix));
    }

    private static void AnalyzeNamespace(SyntaxNodeAnalysisContext context)
    {
        var name = ((BaseNamespaceDeclarationSyntax)context.Node).Name;
        var folder = name.ToString().Split('.').FirstOrDefault(PatternFolders.Contains);

        if (folder is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoPatternNames, name.GetLocation(), name.ToString(), folder));
    }

    private static void AnalyzeMember(SymbolAnalysisContext context, ShippedApi shipped)
    {
        var member = context.Symbol;

        if (member.IsImplicitlyDeclared || !IsNamedHere(member) || shipped.Contains(member) || member.Locations.FirstOrDefault() is not { IsInSource: true } location)
            return;

        if (member is IMethodSymbol { MethodKind: not MethodKind.Ordinary } || member is IFieldSymbol { IsImplicitlyDeclared: true })
            return;

        var type = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IMethodSymbol method => Unwrapped(method.ReturnType),
            _ => null
        };

        if (type is null)
            return;

        if (member is not IFieldSymbol && type.SpecialType == SpecialType.System_Boolean && !AsksAQuestion(member.Name))
            context.ReportDiagnostic(Diagnostic.Create(Rules.BooleansAsk, location, member.Name));

        if (member is not IMethodSymbol && TypeInName(member.Name, type) is { } repeated)
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoTypesInNames, location, member.Name, repeated));
    }

    private static void AnalyzeLocal(SyntaxNodeAnalysisContext context)
    {
        var model = context.SemanticModel;
        var cancellation = context.CancellationToken;

        (SyntaxToken Identifier, ISymbol? Symbol) declared = context.Node switch
        {
            VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax or UsingStatementSyntax or ForStatementSyntax } declarator
                => (declarator.Identifier, model.GetDeclaredSymbol(declarator, cancellation)),
            ParameterSyntax parameter => (parameter.Identifier, model.GetDeclaredSymbol(parameter, cancellation)),
            ForEachStatementSyntax loop => (loop.Identifier, model.GetDeclaredSymbol(loop, cancellation)),
            _ => (default, null)
        };

        var type = declared.Symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter when IsNamedHere(parameter.ContainingSymbol) => parameter.Type,
            _ => null
        };

        var identifier = declared.Identifier;

        if (type is not null && TypeInName(identifier.ValueText, type) is { } repeated)
            context.ReportDiagnostic(Diagnostic.Create(Rules.NoTypesInNames, identifier.GetLocation(), identifier.ValueText, repeated));
    }

    /// <summary>The type word a name ends with, when the declared type is of that kind.</summary>
    private static string? TypeInName(string name, ITypeSymbol type)
    {
        var bare = name.TrimStart('_', '@');

        string? Ends(string word) =>
            bare.Length > word.Length && bare.EndsWith(word, StringComparison.Ordinal) && char.IsLower(bare[bare.Length - word.Length - 1]) ? word : null;

        if (type.SpecialType == SpecialType.System_String)
            return Ends("String") ?? Ends("Str");

        if (type is IArrayTypeSymbol)
            return Ends("Array") ?? Ends("Arr") ?? Ends("List");

        if (Implements(type, "IDictionary`2") || Implements(type, "IReadOnlyDictionary`2"))
            return Ends("Dictionary") ?? Ends("Dict") ?? Ends("Map");

        if (Implements(type, "IEnumerable`1"))
            return Ends("List") ?? Ends("Collection") ?? Ends("Array");

        return null;
    }

    private static bool Implements(ITypeSymbol type, string metadataName) =>
        type.OriginalDefinition.MetadataName == metadataName || type.AllInterfaces.Any(contract => contract.OriginalDefinition.MetadataName == metadataName);

    private static ITypeSymbol Unwrapped(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } awaited && awaited.Name is "Task" or "ValueTask" ? awaited.TypeArguments[0] : type;

    private static bool AsksAQuestion(string name)
    {
        var bare = name.EndsWith("Async", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;

        return Questions.Any(question => bare.StartsWith(question, StringComparison.Ordinal)
            && (bare.Length == question.Length || char.IsUpper(bare[question.Length]) || char.IsDigit(bare[question.Length])))
            || IsThirdPersonVerb(FirstWord(bare));
    }

    /// <summary>
    /// A first word like Forces, Avoids, Preserves or Mentions asks "does it…?" as plainly as one from the list does; a word
    /// ending in -ss, -us, -is or -ous (Class, Status, Axis, Previous) is not a verb. A plural noun slips through, which
    /// costs less than a wrong error under warnings as errors.
    /// </summary>
    private static bool IsThirdPersonVerb(string word) =>
        word.Length > 3
        && word.EndsWith("s", StringComparison.Ordinal)
        && !word.EndsWith("ss", StringComparison.Ordinal)
        && !word.EndsWith("us", StringComparison.Ordinal)
        && !word.EndsWith("is", StringComparison.Ordinal);

    private static string FirstWord(string name)
    {
        var end = 1;

        while (end < name.Length && !char.IsUpper(name[end]) && !char.IsDigit(name[end]))
            end++;

        return name.Substring(0, end);
    }

    private static bool IsNamedHere(ISymbol member) => !member.IsOverride && !IsImplementation(member);

    private static bool IsImplementation(ISymbol member) =>
        member.ContainingType is { } type && type.AllInterfaces
            .SelectMany(contract => contract.GetMembers())
            .Any(contract => SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(contract), member));
}

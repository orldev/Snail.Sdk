using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Snail.Sdk.Analyzers;

/// <summary>Silences two findings a library cannot act on: a released name, and the code Razor writes for it.</summary>
/// <remarks>
/// IDE1006 holds an asynchronous method to the Async suffix, and S3L0006–S3L0008 already spare the names in
/// PublicAPI.Shipped.txt; without this, a shipped method named before the rule could only be renamed — a break for its
/// users — or carried as a NoWarn that hides the rule from every new method too. RS0041 asks public members not to use
/// oblivious types, and the BuildRenderTree that Razor generates for every component is written without nullable
/// annotations: the finding is about code no one in the repository wrote.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Released : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor ShippedName = new(
        "S3LS0001",
        "IDE1006",
        "The name was released in PublicAPI.Shipped.txt: renaming it breaks the library's users, a decision for a major version");

    private static readonly SuppressionDescriptor RazorOutput = new(
        "S3LS0002",
        "RS0041",
        "Razor generates the component's code without nullable annotations");

    /// <inheritdoc />
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => [ShippedName, RazorOutput];

    /// <inheritdoc />
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        var shipped = ShippedApi.Of(context.Options, context.CancellationToken);

        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id == RazorOutput.SuppressedDiagnosticId && IsRazorOutput(diagnostic.Location))
                context.ReportSuppression(Suppression.Create(RazorOutput, diagnostic));
            else if (diagnostic.Id == ShippedName.SuppressedDiagnosticId && Declared(context, diagnostic.Location) is { } symbol && shipped.Contains(symbol))
                context.ReportSuppression(Suppression.Create(ShippedName, diagnostic));
        }
    }

    /// <summary>Whether the location is in a file the Razor source generator wrote: its hint names end in _razor.g.cs.</summary>
    private static bool IsRazorOutput(Location location)
    {
        var path = location.SourceTree?.FilePath ?? string.Empty;

        return path.EndsWith("_razor.g.cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".razor.g.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The symbol the reported name declares: the nearest declaration around it, when its name is the very span reported —
    /// a finding inside a member's body is not about the member.
    /// </summary>
    private static ISymbol? Declared(SuppressionAnalysisContext context, Location location)
    {
        if (location.SourceTree is not { } tree)
            return null;

        var model = context.GetSemanticModel(tree);
        var node = tree.GetRoot(context.CancellationToken).FindNode(location.SourceSpan);

        for (var current = node; current is not null; current = current.Parent)
        {
            if (model.GetDeclaredSymbol(current, context.CancellationToken) is { } symbol)
                return symbol.Locations.Any(declared => declared.SourceTree == tree && declared.SourceSpan == location.SourceSpan) ? symbol : null;
        }

        return null;
    }
}

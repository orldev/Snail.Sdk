using System.Text;

namespace Snail.Sdk.Conformance;

/// <summary>The findings as text: a line each for one repository, a table for the fleet.</summary>
internal static class Report
{
    /// <summary>
    /// One repository's findings, the unmet first, as the console shows them; then each unmet one again as an MSBuild
    /// line — error S3LC0001: check: detail, advice as a warning — which the build that runs the tool reports as its
    /// own error or warning, with the id.
    /// </summary>
    public static string Of(Repository repository, IReadOnlyList<Finding> findings)
    {
        var text = new StringBuilder();
        var ordered = findings.OrderBy(finding => finding.IsMet).ThenBy(finding => finding.Severity).ThenBy(finding => finding.Id, StringComparer.Ordinal).ToList();

        text.AppendLine($"{repository.Name} ({repository.Kind.ToString().ToLowerInvariant()})");

        foreach (var finding in ordered)
            text.AppendLine($"  {Mark(finding)} {finding.Id} {finding.Check}{(finding.Detail.Length > 0 ? $": {finding.Detail}" : string.Empty)}");

        foreach (var finding in ordered.Where(finding => !finding.IsMet))
            text.AppendLine(Canonical(finding));

        return text.ToString();
    }

    /// <summary>An unmet finding as MSBuild's canonical message: error or warning, the id, the check and what was seen.</summary>
    public static string Canonical(Finding finding) =>
        $"{(finding.Severity is Severity.Required ? "error" : "warning")} {finding.Id}: {finding.Check}: {finding.Detail}";

    /// <summary>The fleet as Markdown: a row per repository, a column per check, then what each unmet check saw.</summary>
    public static string Fleet(IReadOnlyList<(Repository Repository, IReadOnlyList<Finding> Findings)> fleet)
    {
        var checks = fleet.SelectMany(row => row.Findings).Select(finding => finding.Check).Distinct(StringComparer.Ordinal).ToList();
        var page = new StringBuilder();
        var conforming = fleet.Count(row => IsConforming(row.Findings));

        page.AppendLine("# Conformance").AppendLine();
        page.AppendLine($"{conforming} of {fleet.Count} repositories conform. ✓ holds · ✗ fails the build · ○ advice, reported only · — not asked of this kind.").AppendLine();
        page.AppendLine($"| Repository | Kind | {string.Join(" | ", checks)} |");
        page.AppendLine($"| --- | --- | {string.Join(" | ", checks.Select(_ => ":-:"))} |");

        foreach (var (repository, findings) in fleet)
        {
            var cells = checks.Select(check => findings.FirstOrDefault(finding => finding.Check == check) is { } found ? Mark(found) : "—");

            page.AppendLine($"| {repository.Name} | {repository.Kind.ToString().ToLowerInvariant()} | {string.Join(" | ", cells)} |");
        }

        foreach (var (repository, findings) in fleet.Where(row => row.Findings.Any(finding => !finding.IsMet)))
        {
            page.AppendLine().AppendLine($"## {repository.Name}").AppendLine();

            foreach (var finding in findings.Where(finding => !finding.IsMet))
                page.AppendLine($"- {Mark(finding)} **{finding.Check}** ({finding.Id}): {finding.Detail}");
        }

        return page.ToString();
    }

    /// <summary>Whether every required check holds.</summary>
    public static bool IsConforming(IReadOnlyList<Finding> findings) =>
        findings.All(finding => finding.IsMet || finding.Severity is Severity.Advice);

    private static string Mark(Finding finding) => finding switch
    {
        { IsMet: true } => "✓",
        { Severity: Severity.Advice } => "○",
        _ => "✗"
    };
}

using System.Text;
using System.Text.Json;

namespace Snail.Sdk.Docs;

/// <summary>The catalog as Markdown: an index of every tool, a page per family, and the prompts and resources.</summary>
/// <remarks>
/// Merged from the generators Figma, Blender and AfterEffects each kept: the behaviour hints and the output schema from
/// Figma's, the skills and the escaping of descriptions from the other two. What differed between them is now
/// docs/tools.json.
/// </remarks>
internal static class ToolPages
{
    private const int LongestSummary = 160;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The index: the count, the server's own words, every tool with the first sentence of its description.</summary>
    public static string Index(Catalog catalog, CatalogSettings settings)
    {
        var families = catalog.Families;
        var total = families.Sum(family => family.Tools.Count);
        var withSkills = families.Any(family => family.Skill is not null);
        var always = families.Where(family => family.Skill is null).Sum(family => family.Tools.Count);
        var page = new StringBuilder();

        page.AppendLine("# Tools").AppendLine();
        var counted = $"{Counted(total, "tool", "tools")} in {Counted(families.Count, "family", "families")}";

        page.AppendLine(withSkills ? $"{counted}: {always} always on, the rest in skills that load on demand." : $"{counted}.");
        page.AppendLine("The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.");

        foreach (var paragraph in settings.Intro)
            page.AppendLine().AppendLine(paragraph);

        page.AppendLine();
        page.AppendLine(withSkills ? "| Tool | Family | Skill | What it does |" : "| Tool | Family | What it does |");
        page.AppendLine(withSkills ? "| --- | --- | --- | --- |" : "| --- | --- | --- |");

        foreach (var family in families)
        {
            foreach (var tool in family.Tools)
                page.AppendLine(IndexRow(family, tool, withSkills));
        }

        AppendOthers(page, "Prompts", catalog.Prompts);
        AppendOthers(page, "Resources", catalog.Resources);

        return page.ToString();
    }

    /// <summary>A family's page: each tool with its description, behaviour, parameters and schemas.</summary>
    public static string Family(ToolFamily family, CatalogSettings settings)
    {
        var page = new StringBuilder();

        page.AppendLine($"# {family.Title}").AppendLine();

        if (family.Skill is not null)
            page.AppendLine(settings.SkillNote.Replace("{skill}", family.Skill, StringComparison.Ordinal)).AppendLine();

        foreach (var tool in family.Tools)
        {
            page.AppendLine($"## <code>{Seams(tool.Name)}</code>").AppendLine();
            page.AppendLine(Escape(tool.Description)).AppendLine();

            AppendBehaviour(page, tool.Wire, settings);
            AppendParameters(page, tool.Wire);
            AppendSchema(page, "JSON Schema", tool.Wire, "inputSchema");
            AppendSchema(page, "Output schema", tool.Wire, "outputSchema");
        }

        return page.ToString();
    }

    private static string Counted(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    private static string IndexRow(ToolFamily family, CatalogEntry tool, bool withSkills)
    {
        var link = $"[`{tool.Name}`]({family.Slug}.md#{tool.Name})";
        var summary = Cell(FirstSentence(tool.Description));

        return withSkills
            ? $"| {link} | {family.Title} | {family.Skill ?? "—"} | {summary} |"
            : $"| {link} | {family.Title} | {summary} |";
    }

    private static void AppendOthers(StringBuilder page, string title, IReadOnlyList<CatalogEntry> entries)
    {
        if (entries.Count == 0)
            return;

        page.AppendLine().AppendLine($"## {title}").AppendLine();
        page.AppendLine("| Name | What it does |").AppendLine("| --- | --- |");

        foreach (var entry in entries)
            page.AppendLine($"| `{Address(entry)}` | {Cell(entry.Description)} |");
    }

    private static string Address(CatalogEntry entry) =>
        entry.Wire.TryGetProperty("uriTemplate", out var template) ? template.GetString() ?? entry.Name : entry.Name;

    /// <summary>The four behaviour hints as one line of plain words, in the order a host weighs them: what it touches, how it touches it, whether a repeat is safe, how far it reaches.</summary>
    private static void AppendBehaviour(StringBuilder page, JsonElement tool, CatalogSettings settings)
    {
        if (!tool.TryGetProperty("annotations", out var hints))
            return;

        var words = new[]
        {
            IsSet(hints,"readOnlyHint") ? "read-only" : IsSet(hints,"destructiveHint") ? "may overwrite or delete" : "only adds",
            IsSet(hints,"idempotentHint") ? "repeat-safe" : "every call adds",
            IsSet(hints,"openWorldHint") ? settings.OpenWorld : settings.ClosedWorld
        };

        page.AppendLine($"*{string.Join(" · ", words)}.*").AppendLine();
    }

    private static bool IsSet(JsonElement hints, string name) =>
        hints.TryGetProperty(name, out var hint) && hint.ValueKind is JsonValueKind.True;

    private static void AppendParameters(StringBuilder page, JsonElement tool)
    {
        if (!tool.TryGetProperty("inputSchema", out var schema) || !schema.TryGetProperty("properties", out var properties) || !properties.EnumerateObject().Any())
        {
            page.AppendLine("Takes no parameters.").AppendLine();

            return;
        }

        var required = schema.TryGetProperty("required", out var names)
            ? names.EnumerateArray().Select(name => name.GetString()).ToHashSet(StringComparer.Ordinal)
            : [];

        page.AppendLine("| Parameter | Type | Required | Default | Description |");
        page.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var property in properties.EnumerateObject())
        {
            var description = property.Value.TryGetProperty("description", out var text) ? text.GetString() ?? string.Empty : string.Empty;
            var isRequired = required.Contains(property.Name) ? "yes" : "—";

            page.AppendLine($"| `{property.Name}` | {TypeOf(property.Value)} | {isRequired} | {DefaultOf(property.Value)} | {Cell(description)} |");
        }

        page.AppendLine();
    }

    private static void AppendSchema(StringBuilder page, string title, JsonElement tool, string property)
    {
        if (!tool.TryGetProperty(property, out var schema))
            return;

        page.AppendLine($"<details><summary>{title}</summary>").AppendLine();
        page.AppendLine("```json").AppendLine(JsonSerializer.Serialize(schema, Indented)).AppendLine("```").AppendLine();
        page.AppendLine("</details>").AppendLine();
    }

    private static string TypeOf(JsonElement property)
    {
        if (property.TryGetProperty("enum", out var choices))
            return string.Join(" \\| ", choices.EnumerateArray().Select(choice => $"`{choice}`"));

        if (!property.TryGetProperty("type", out var type))
            return "`any`";

        if (type.ValueKind is JsonValueKind.String)
            return $"`{type.GetString()}`";

        var names = type.EnumerateArray().Select(name => name.GetString()).ToList();
        var isNullable = names.Remove("null");

        return $"`{string.Join(" \\| ", names)}{(isNullable ? "?" : string.Empty)}`";
    }

    private static string DefaultOf(JsonElement property)
    {
        if (!property.TryGetProperty("default", out var value))
            return "—";

        return value.ValueKind is JsonValueKind.String ? $"`{value.GetString()}`" : $"`{value.GetRawText()}`";
    }

    private static string FirstSentence(string description)
    {
        var end = description.IndexOf(". ", StringComparison.Ordinal);
        var sentence = end < 0 ? description : description[..(end + 1)];

        return sentence.Length <= LongestSummary ? sentence : $"{sentence[..(LongestSummary - 3)]}…";
    }

    /// <summary>The name with a line-break opportunity after every underscore: a heading in a narrow margin would otherwise break mid-word.
    /// A &lt;wbr&gt; carries no character, so the anchor, the search index and a copied name all stay the plain name.</summary>
    private static string Seams(string name) => name.Replace("_", "_<wbr>", StringComparison.Ordinal);

    private static string Cell(string text) =>
        Escape(text).Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ").Trim();

    /// <summary>A description as text rather than markup: a wire copy that keeps <c>files/&lt;name&gt;</c> would otherwise pass an element to the page.</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}

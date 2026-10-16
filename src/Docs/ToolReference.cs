using System.Text.Json;
using System.Text.Json.Nodes;

namespace Snail.Sdk.Docs;

/// <summary>A server's reference: tools/index.md, a page per family in tools/, and schemas/tools.json, under one folder.</summary>
internal static class ToolReference
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Writes the reference of a built server and answers with what was written.</summary>
    public static string Write(string assemblyPath, string settingsPath, string outputFolder)
    {
        var settings = CatalogSettings.Read(settingsPath);
        var context = ServerContext.Open(assemblyPath);
        var catalog = ToolCatalog.Read(context, settings);

        var tools = Directory.CreateDirectory(Path.Combine(outputFolder, "tools")).FullName;
        var schemas = Directory.CreateDirectory(Path.Combine(outputFolder, "schemas")).FullName;
        var written = new List<string> { Written(Path.Combine(tools, "index.md"), ToolPages.Index(catalog, settings)) };

        foreach (var family in catalog.Families)
            written.Add(Written(Path.Combine(tools, $"{family.Slug}.md"), ToolPages.Family(family, settings)));

        written.Add(Written(Path.Combine(schemas, "tools.json"), Schemas(catalog)));
        RemoveStale(tools, written);

        var count = catalog.Families.Sum(family => family.Tools.Count);

        return $"{count} tools in {catalog.Families.Count} families, {catalog.Prompts.Count} prompts, {catalog.Resources.Count} resources -> {outputFolder}";
    }

    /// <summary>Deletes the pages of families that no longer exist: a page left behind would still be built and found by the search, with tools the server no longer has.</summary>
    private static void RemoveStale(string tools, List<string> written)
    {
        var current = written.Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        var stale = Directory.EnumerateFiles(tools, "*.md").Where(page => !current.Contains(Path.GetFullPath(page))).ToList();

        foreach (var page in stale)
            File.Delete(page);
    }

    private static string Schemas(Catalog catalog)
    {
        var array = new JsonArray();

        foreach (var family in catalog.Families)
        {
            foreach (var tool in family.Tools)
            {
                var entry = JsonNode.Parse(tool.Wire.GetRawText())!.AsObject();

                entry["skill"] = family.Skill;
                array.Add(entry);
            }
        }

        return array.ToJsonString(Indented);
    }

    private static string Written(string path, string content)
    {
        File.WriteAllText(path, content);

        return path;
    }
}

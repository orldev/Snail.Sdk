using System.Text.RegularExpressions;

namespace Snail.Sdk.Docs;

/// <summary>An entry of a site's navigation: one page, or a section of pages under a title.</summary>
/// <param name="Title">The title shown; null for a page, which then shows its own first heading.</param>
/// <param name="Page">The page, for an entry of one page.</param>
/// <param name="Pages">The pages, for a section.</param>
internal sealed record NavigationEntry(string? Title, string? Page, IReadOnlyList<string> Pages);

/// <summary>
/// The navigation of a site made from docs/: the home page, the pages beside it, a section per folder, the reference
/// last. Inside a folder the pages follow the order its index.md links them in — a tool reference's index already lists
/// the families as docs/tools.json orders them — and the rest follow by name.
/// </summary>
/// <remarks>
/// The theme shows one level of sections under the header, so a folder is one section however deep its pages lie, named
/// after the folder that holds them all: reference/ with nothing but tools/ in it is the section Tools.
/// </remarks>
internal static partial class SiteNavigation
{
    private const string Home = "index.md";
    private const string Reference = "reference";

    /// <summary>The navigation over the pages copied into the content folder.</summary>
    public static IReadOnlyList<NavigationEntry> Of(string content, IReadOnlyList<string> pages)
    {
        var entries = new List<NavigationEntry> { new("Home", Home, []) };
        var top = pages.Where(page => !page.Contains('/', StringComparison.Ordinal)).ToList();

        entries.AddRange(Ordered(content, string.Empty, top).Where(page => page != Home).Select(page => new NavigationEntry(null, page, [])));

        var folders = pages
            .Where(page => page.Contains('/', StringComparison.Ordinal))
            .GroupBy(page => page[..page.IndexOf('/', StringComparison.Ordinal)])
            .OrderBy(folder => folder.Key == Reference ? 1 : 0)
            .ThenBy(folder => folder.Key, StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            var inside = Flattened(content, folder.Key, [.. folder]);
            var title = Title(folder.Key, inside);

            entries.Add(inside.Count == 1 ? new NavigationEntry(title, inside[0], []) : new NavigationEntry(title, null, inside));
        }

        return entries;
    }

    /// <summary>The pages of a folder and of every folder below it, each folder's own first.</summary>
    private static List<string> Flattened(string content, string folder, IReadOnlyList<string> pages)
    {
        var prefix = $"{folder}/";
        var own = pages.Where(page => !page[prefix.Length..].Contains('/', StringComparison.Ordinal)).ToList();
        var flattened = Ordered(content, folder, own);

        var below = pages
            .Except(own)
            .GroupBy(page => prefix + page[prefix.Length..][..page[prefix.Length..].IndexOf('/', StringComparison.Ordinal)])
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var group in below)
            flattened.AddRange(Flattened(content, group.Key, [.. group]));

        return flattened;
    }

    /// <summary>The pages of one folder: its index.md, then what the index links in that order, then the rest by name.</summary>
    private static List<string> Ordered(string content, string folder, IReadOnlyList<string> pages)
    {
        var index = folder.Length == 0 ? Home : $"{folder}/{Home}";
        var ordered = new List<string>();

        if (pages.Contains(index))
        {
            ordered.Add(index);

            foreach (Match link in Link().Matches(File.ReadAllText(Path.Combine(content, index))))
            {
                var linked = Joined(folder, Uri.UnescapeDataString(link.Groups["path"].Value));

                if (pages.Contains(linked) && !ordered.Contains(linked))
                    ordered.Add(linked);
            }
        }

        ordered.AddRange(pages.Except(ordered).Order(StringComparer.Ordinal));

        return ordered;
    }

    /// <summary>The section's title: the folder holding all its pages, in words.</summary>
    private static string Title(string folder, IReadOnlyList<string> pages)
    {
        var common = folder;

        while (true)
        {
            var prefix = $"{common}/";
            var next = pages.Select(page => page[prefix.Length..]).Select(rest => rest.Contains('/', StringComparison.Ordinal) ? rest[..rest.IndexOf('/', StringComparison.Ordinal)] : null).Distinct().ToList();

            if (next is not [{ } only])
                break;

            common = $"{common}/{only}";
        }

        var name = common[(common.LastIndexOf('/') + 1)..].Replace('-', ' ').Replace('_', ' ');

        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>A path relative to a folder of the content, resolved to a path from the content's root.</summary>
    private static string Joined(string folder, string path)
    {
        var parts = new List<string>(folder.Length == 0 ? [] : folder.Split('/'));

        foreach (var part in path.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
            }
            else if (part is not ("." or ""))
            {
                parts.Add(part);
            }
        }

        return string.Join('/', parts);
    }

    [GeneratedRegex(@"\]\((?<path>[^()\s#?:]+\.md)(?:[#?][^()\s]*)?\)")]
    private static partial Regex Link();
}

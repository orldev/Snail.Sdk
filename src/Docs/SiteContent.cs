using System.Text.RegularExpressions;

namespace Snail.Sdk.Docs;

/// <summary>
/// The pages of a site made from what a repository already has: docs/ as it stands, and README.md as the home page when
/// docs/ has no index.md of its own.
/// </summary>
/// <remarks>
/// The README is written for GitHub, where docs/usage.md and src/Program.cs are both one click away. On the site only
/// docs/ exists, so every link is read again from the page's new place: a link into docs/ stays a link between pages,
/// a link to any other file of the repository goes to that file on GitHub, and without a known repository it becomes its
/// own words — a strict build would refuse a link to a page the site does not have.
/// </remarks>
internal static partial class SiteContent
{
    private const string Home = "index.md";

    /// <summary>What docs/ keeps for the build rather than for a reader, never copied to the site.</summary>
    private static readonly HashSet<string> Settings = new(StringComparer.Ordinal) { "mkdocs.yml", "tools.json", "requirements.txt", ".nojekyll", "theme", "site" };

    /// <summary>Copies the pages into the folder, replacing what an earlier build left there, and answers with each page's path in it.</summary>
    public static IReadOnlyList<string> Copy(SiteFacts facts, string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        Directory.CreateDirectory(folder);

        var docs = Path.Combine(facts.Repository, "docs");
        var pages = new List<string>();

        foreach (var file in Published(docs))
        {
            var relative = Slashed(Path.GetRelativePath(docs, file));
            var target = Path.Combine(folder, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (!relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, target);

                continue;
            }

            File.WriteAllText(target, Relinked(File.ReadAllText(file), $"docs/{relative}", facts));
            pages.Add(relative);
        }

        if (!pages.Contains(Home))
        {
            File.WriteAllText(Path.Combine(folder, Home), HomePage(facts));
            pages.Add(Home);
        }

        return pages;
    }

    /// <summary>The site's name: the one the build gives, the README's first heading, or the repository's folder.</summary>
    public static string Name(SiteFacts facts)
    {
        if (facts.Name.Length > 0)
            return facts.Name;

        var readme = Path.Combine(facts.Repository, "README.md");
        var heading = File.Exists(readme) ? File.ReadLines(readme).FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal)) : null;

        return heading is null ? Path.GetFileName(facts.Repository) : heading[2..].Trim();
    }

    /// <summary>A page's text with every link read from its new place; <paramref name="source"/> is its path in the repository.</summary>
    public static string Relinked(string text, string source, SiteFacts facts)
    {
        var fenced = false;
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (Fence().Match(lines[i]) is { Success: true } fence)
            {
                if (!fenced)
                    lines[i] = fence.Groups["open"].Value + fence.Groups["language"].Value;

                fenced = !fenced;

                continue;
            }

            if (fenced)
                continue;

            lines[i] = Definition().Replace(lines[i], match => match.Groups["start"].Value + (Relinked(match.Groups["target"].Value, source, facts, image: false) ?? match.Groups["target"].Value));
            lines[i] = Inline().Replace(lines[i], match => Replaced(match, source, facts));
            lines[i] = Attribute().Replace(lines[i], match => match.Groups["start"].Value + (Relinked(match.Groups["target"].Value, source, facts, image: match.Groups["name"].Value == "src") ?? match.Groups["target"].Value) + "\"");
        }

        return string.Join('\n', lines);
    }

    private static string Replaced(Match match, string source, SiteFacts facts)
    {
        if (match.Groups["code"].Success)
            return match.Value;

        var image = match.Groups["bang"].Value.Length > 0;
        var target = match.Groups["target"].Value;
        var relinked = Relinked(target, source, facts, image);

        if (relinked is null)
            return match.Groups["text"].Value;

        return $"{match.Groups["bang"].Value}[{match.Groups["text"].Value}]({relinked}{match.Groups["title"].Value})";
    }

    /// <summary>
    /// The link from the page's new place: unchanged when it is a web address or names no file of the repository, null —
    /// its words alone — when it leaves the repository, which neither the site nor GitHub can follow, or names a file
    /// the site does not have and no repository is known to link to.
    /// </summary>
    private static string? Relinked(string target, string source, SiteFacts facts, bool image)
    {
        if (target.Length == 0 || target.StartsWith('#') || target.StartsWith('/') || Scheme().IsMatch(target))
            return target;

        var cut = target.IndexOfAny(['#', '?']);
        var path = Uri.UnescapeDataString(cut < 0 ? target : target[..cut]);
        var rest = cut < 0 ? string.Empty : target[cut..];
        var full = Slashed(Path.GetRelativePath(facts.Repository, Path.GetFullPath(Path.Combine(facts.Repository, Path.GetDirectoryName(source) ?? string.Empty, path))));

        if (full.StartsWith("..", StringComparison.Ordinal))
            return null;

        var onDisk = Path.Combine(facts.Repository, full);

        if (!File.Exists(onDisk) && !Directory.Exists(onDisk))
            return target;

        var place = Place(source);

        if (full == "README.md" && !File.Exists(Path.Combine(facts.Repository, "docs", Home)))
            return Between(place, Home) + rest;

        if (InDocs(full) is { } page && IsPublished(page) && Published(page, onDisk) is { } published)
            return Between(place, published) + rest;

        if (facts.RepositoryUrl.Length == 0)
            return null;

        var kind = Directory.Exists(onDisk) ? "tree" : image ? "raw" : "blob";

        return $"{facts.RepositoryUrl}/{kind}/HEAD/{Uri.EscapeDataString(full).Replace("%2F", "/", StringComparison.Ordinal)}{rest}";
    }

    /// <summary>The path under docs/, or null for a path outside it; docs/ itself is the empty path.</summary>
    private static string? InDocs(string full) =>
        full == "docs" ? string.Empty : full.StartsWith("docs/", StringComparison.Ordinal) ? full["docs/".Length..] : null;

    /// <summary>The page a path under docs/ is on the site: a file is itself, a folder its index.md, docs/ itself the home page.</summary>
    private static string? Published(string page, string onDisk)
    {
        if (!Directory.Exists(onDisk))
            return page;

        if (page.Length == 0)
            return Home;

        return File.Exists(Path.Combine(onDisk, Home)) ? $"{page.TrimEnd('/')}/{Home}" : null;
    }

    /// <summary>Where a page of the repository lands on the site: a page of docs/ keeps its place under it, anything else is the home page.</summary>
    private static string Place(string source) =>
        source.StartsWith("docs/", StringComparison.Ordinal) ? source["docs/".Length..] : Home;

    private static string Between(string from, string to)
    {
        var folder = Path.GetDirectoryName(from) ?? string.Empty;

        return Slashed(Path.GetRelativePath(folder.Length == 0 ? "." : folder, to));
    }

    private static string HomePage(SiteFacts facts)
    {
        var readme = Path.Combine(facts.Repository, "README.md");

        return File.Exists(readme)
            ? Titled(Relinked(File.ReadAllText(readme), "README.md", facts), facts)
            : $"# {Name(facts)}\n\n{facts.Description}\n";
    }

    /// <summary>
    /// The home page with a title of the first level. The theme sets every lower heading in the margin beside its section,
    /// so a README that opens with ## would open the site on an empty column with its name in small print: that heading
    /// becomes the title, and a README with none gets the site's name.
    /// </summary>
    private static string Titled(string text, SiteFacts facts)
    {
        var fenced = false;

        foreach (var line in text.Split('\n'))
        {
            if (Fence().IsMatch(line))
                fenced = !fenced;
            else if (!fenced && line.StartsWith("# ", StringComparison.Ordinal))
                return text;
        }

        var opening = text.TrimStart();

        return opening.StartsWith("## ", StringComparison.Ordinal)
            ? opening[1..]
            : $"# {Name(facts)}\n\n{text}";
    }

    private static IEnumerable<string> Published(string docs) =>
        Directory.Exists(docs)
            ? Directory.EnumerateFiles(docs, "*", SearchOption.AllDirectories)
                .Where(file => IsPublished(Slashed(Path.GetRelativePath(docs, file))))
                .Order(StringComparer.Ordinal)
            : [];

    /// <summary>Whether a path under docs/ reaches the site: not a build setting, not hidden.</summary>
    private static bool IsPublished(string relative)
    {
        var parts = relative.Split('/');

        return !Settings.Contains(parts[0]) && !parts.Any(part => part.StartsWith('.'));
    }

    private static string Slashed(string path) => path.Replace('\\', '/');

    /// <summary>A fence, with the first word of its info string: GitHub reads only that word, and so must the site.</summary>
    [GeneratedRegex(@"^(?<open>\s{0,3}(```+|~~~+))\s*(?<language>[^\s`]*)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^(?<start>\s{0,3}\[[^\]]+\]:\s*)(?<target>\S+)")]
    private static partial Regex Definition();

    [GeneratedRegex(@"(?<code>`+[^`]*`+)|(?<bang>!?)\[(?<text>(?:[^\[\]]|\[[^\]]*\])*)\]\((?<target>[^()\s]*(?:\([^()\s]*\)[^()\s]*)*)(?<title>\s+""[^""]*"")?\)")]
    private static partial Regex Inline();

    [GeneratedRegex(@"(?<start>\b(?<name>src|href)="")(?<target>[^""]*)""")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();
}

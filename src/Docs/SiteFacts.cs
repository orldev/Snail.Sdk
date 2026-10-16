using System.Text.RegularExpressions;

namespace Snail.Sdk.Docs;

/// <summary>What the build knows about the repository a site is made for, read from the file the SnailSite target writes.</summary>
/// <remarks>
/// The facts travel in a file of <c>key=value</c> lines rather than on the command line: a description holds quotes,
/// semicolons and line breaks that no command line carries whole on every platform.
/// </remarks>
internal sealed partial record SiteFacts
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "repository", "theme", "name", "description", "repository-url", "license" };

    /// <summary>The repository root: the folder of global.json.</summary>
    public required string Repository { get; init; }

    /// <summary>The theme folder the SDK carries.</summary>
    public required string Theme { get; init; }

    /// <summary>The site's name; the README's first heading, or the repository's folder, when the build gives none.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>One sentence for the description of every page.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The repository on GitHub, https://github.com/owner/name; empty when the build does not know it.</summary>
    public string RepositoryUrl { get; init; } = string.Empty;

    /// <summary>The licence the footer names.</summary>
    public string License { get; init; } = string.Empty;

    /// <summary>
    /// Reads the facts. A line opens a fact when it starts with a known key and <c>=</c>; any other line continues the
    /// fact before it, as a description written over several lines in a project file arrives.
    /// </summary>
    public static SiteFacts Read(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var key = string.Empty;

        foreach (var line in File.ReadAllLines(path))
        {
            var split = line.IndexOf('=', StringComparison.Ordinal);

            if (split > 0 && Keys.Contains(line[..split]))
            {
                key = line[..split];
                values[key] = line[(split + 1)..];
            }
            else if (key.Length > 0)
            {
                values[key] += $" {line}";
            }
        }

        return new SiteFacts
        {
            Repository = Path.GetFullPath(Value(values, "repository", ".")),
            Theme = Path.GetFullPath(Value(values, "theme", ".")),
            Name = Value(values, "name", string.Empty),
            Description = Value(values, "description", string.Empty),
            RepositoryUrl = WithoutGitSuffix(Value(values, "repository-url", string.Empty).TrimEnd('/')),
            License = Value(values, "license", string.Empty)
        };
    }

    /// <summary>owner/name, when the repository is on GitHub.</summary>
    public string? GitHubName =>
        Uri.TryCreate(RepositoryUrl, UriKind.Absolute, out var url) && url.Host == "github.com" && url.AbsolutePath.Trim('/').Split('/') is [var owner, var name]
            ? $"{owner}/{name}"
            : null;

    /// <summary>A fact with its whitespace collapsed to single spaces, or the fallback when it is missing.</summary>
    private static string Value(Dictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) ? Whitespace().Replace(value, " ").Trim() : fallback;

    private static string WithoutGitSuffix(string url) => url.EndsWith(".git", StringComparison.Ordinal) ? url[..^4] : url;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

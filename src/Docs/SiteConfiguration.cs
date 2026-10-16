using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snail.Sdk.Docs;

/// <summary>The MkDocs configuration of a Snail site, as YAML.</summary>
/// <remarks>
/// Every value is written as a JSON string, which YAML reads as a double-quoted scalar: a description with a colon or a
/// path with a space then needs no rule of its own.
/// </remarks>
internal static partial class SiteConfiguration
{
    private const string Written = "# Written by Snail.Sdk.Docs for the SnailSite target; every build of the site writes it again.";

    private static readonly JsonSerializerOptions Quoting = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The configuration of a site made from docs/ and the README, its pages in <c>content/</c> beside it.</summary>
    public static string Generated(SiteFacts facts, IReadOnlyList<NavigationEntry> navigation)
    {
        var yaml = new StringBuilder();

        yaml.AppendLine(Written);
        yaml.AppendLine($"site_name: {Quoted(SiteContent.Name(facts))}");

        if (facts.Description.Length > 0)
            yaml.AppendLine($"site_description: {Quoted(facts.Description)}");

        if (facts.GitHubName is { } name)
        {
            var (owner, repository) = (name[..name.IndexOf('/', StringComparison.Ordinal)], name[(name.IndexOf('/', StringComparison.Ordinal) + 1)..]);

            yaml.AppendLine($"site_url: {Quoted($"https://{owner}.github.io/{repository}/")}");
            yaml.AppendLine($"repo_url: {Quoted(facts.RepositoryUrl)}");
            yaml.AppendLine($"repo_name: {Quoted(name)}");
        }

        if (facts.License.Length > 0)
            yaml.AppendLine($"copyright: {Quoted(facts.License)}");

        yaml.AppendLine("""
            edit_uri: ""
            docs_dir: content
            """);
        AppendTheme(yaml, facts);
        yaml.AppendLine("""
            plugins:
              - search

            # A page the build writes but the navigation leaves out is a page nobody reaches, and a link to a heading that
            # is not there leads nowhere: with --strict both fail the build instead of passing as a quiet INFO.
            validation:
              nav:
                omitted_files: warn
              links:
                anchors: warn

            markdown_extensions:
              - admonition
              - attr_list
              - tables
              - toc:
                  permalink: true
                  # Headings get the anchors GitHub gives them, so a README's own links to its sections keep working.
                  slugify: !!python/object/apply:pymdownx.slugs.slugify
                    kwds:
                      case: lower
              - pymdownx.details
              - pymdownx.highlight
              - pymdownx.inlinehilite
              - pymdownx.superfences:
                  custom_fences:
                    - name: mermaid
                      class: mermaid
                      format: !!python/name:pymdownx.superfences.fence_code_format

            nav:
            """);

        foreach (var entry in navigation)
        {
            if (entry.Page is { } page)
            {
                yaml.AppendLine(entry.Title is null ? $"  - {Quoted(page)}" : $"  - {Quoted(entry.Title)}: {Quoted(page)}");

                continue;
            }

            yaml.AppendLine($"  - {Quoted(entry.Title ?? string.Empty)}:");

            foreach (var inside in entry.Pages)
                yaml.AppendLine($"      - {Quoted(inside)}");
        }

        return yaml.ToString();
    }

    /// <summary>
    /// The repository's own docs/mkdocs.yml with the Snail theme: MkDocs reads it through INHERIT and lets this file's
    /// keys win, and its docs_dir is named from here, since a relative path is read from the file that is loaded.
    /// </summary>
    public static string Inherited(string own, SiteFacts facts)
    {
        var folder = Path.GetDirectoryName(own)!;
        var docsDirectory = DocsDirectory().Match(File.ReadAllText(own)) is { Success: true } match ? match.Groups["path"].Value : "docs";
        var yaml = new StringBuilder();

        yaml.AppendLine(Written);
        yaml.AppendLine($"INHERIT: {Quoted(own)}");
        yaml.AppendLine($"docs_dir: {Quoted(Path.GetFullPath(Path.Combine(folder, docsDirectory)))}");
        AppendTheme(yaml, facts);

        return yaml.ToString();
    }

    private static void AppendTheme(StringBuilder yaml, SiteFacts facts)
    {
        yaml.AppendLine($"""
            theme:
              name: null
              custom_dir: {Quoted(facts.Theme)}
              # A template with no page of its own is rendered only when listed here: mkdocs_theme.yml would be the usual
              # place, but MkDocs reads it only for a named theme.
              static_templates:
                - 404.html
            """);
    }

    private static string Quoted(string value) => JsonSerializer.Serialize(value, Quoting);

    [GeneratedRegex(@"^docs_dir:\s*[""']?(?<path>[^""'\s#]+)", RegexOptions.Multiline)]
    private static partial Regex DocsDirectory();
}

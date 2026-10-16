namespace Snail.Sdk.Docs;

/// <summary>
/// A Snail site, ready for MkDocs: the configuration and, unless the repository keeps a docs/mkdocs.yml of its own, the
/// pages it is made from, all in a folder of the build's own. The theme is the one the SDK carries, so every library and
/// server site looks the same and changes with the SDK, never by a copy in a repository.
/// </summary>
internal static class Site
{
    /// <summary>Writes the site's configuration and pages into the folder and answers with what was written.</summary>
    public static string Write(SiteFacts facts, string outputFolder)
    {
        var folder = Directory.CreateDirectory(outputFolder).FullName;
        var configuration = Path.Combine(folder, "mkdocs.yml");
        var own = Path.Combine(facts.Repository, "docs", "mkdocs.yml");

        if (File.Exists(own))
        {
            File.WriteAllText(configuration, SiteConfiguration.Inherited(own, facts));

            return $"{own} with the Snail theme -> {configuration}";
        }

        var content = Path.Combine(folder, "content");
        var pages = SiteContent.Copy(facts, content);

        File.WriteAllText(configuration, SiteConfiguration.Generated(facts, SiteNavigation.Of(content, pages)));

        return $"{pages.Count} pages -> {configuration}";
    }
}

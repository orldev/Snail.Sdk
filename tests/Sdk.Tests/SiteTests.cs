namespace Snail.Sdk.Sdk.Tests;

/// <summary>The site a library or server builds with -t:SnailSite: the theme the SDK carries over the repository's own pages.</summary>
/// <remarks>Each build makes MkDocs a virtual environment of its own in obj/, so these tests need python3 and the package index.</remarks>
[Collection(SdkFeed.Collection)]
public sealed class SiteTests(SdkFeed sdk)
{
    private const string Greeting = """
        namespace Library;

        /// <summary>Says hello.</summary>
        public sealed class Greeting
        {
            /// <summary>The greeting.</summary>
            public string Text() => "Hello";
        }
        """;

    private const string PublicApi = """
        #nullable enable
        Library.Greeting
        Library.Greeting.Greeting() -> void
        Library.Greeting.Text() -> string!

        """;

    /// <summary>
    /// Without a docs/mkdocs.yml the README is the home page: a link into docs/ stays a link between pages, a link to code
    /// goes to the file on GitHub, a link out of the repository keeps only its words, a fence reads as GitHub reads it,
    /// and the reference comes last.
    /// </summary>
    [Fact]
    public void SnailSite_WithoutAConfigurationOfItsOwn_MakesOneFromTheReadmeAndDocs()
    {
        var folder = Library("""
            <PropertyGroup>
              <Description>Says hello; it's polite.</Description>
              <RepositoryUrl>https://github.com/orldev/Sample.git</RepositoryUrl>
              <PackageLicenseExpression>MIT</PackageLicenseExpression>
            </PropertyGroup>
            """);
        Write(folder, "README.md", "# Sample\n\nRead [the guide](docs/guide.md#usage), [the code](Greeting.cs) or [a neighbour](../Neighbour).\n\n```json lines\n{}\n```\n\n## Usage\n\nSee [usage](#usage).\n");
        Write(folder, "docs/guide.md", "# Guide\n\n## Usage\n\nCall it.\n");

        var (referenceExitCode, referenceOutput) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true");
        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite");
        var work = Path.Combine(folder, "obj", "snail-site");
        var home = File.ReadAllText(Path.Combine(work, "site", "index.html"));
        var configuration = File.ReadAllText(Path.Combine(work, "mkdocs.yml"));

        Assert.True(referenceExitCode == 0, referenceOutput);
        Assert.True(exitCode == 0, output);
        Assert.Contains("href=\"guide/#usage\"", home, StringComparison.Ordinal);
        Assert.Contains("https://github.com/orldev/Sample/blob/HEAD/Greeting.cs", home, StringComparison.Ordinal);
        Assert.Contains("or a neighbour.", home, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(work, "site", "css", "site.css")));
        Assert.Contains("site_description: \"Says hello; it's polite.\"", configuration, StringComparison.Ordinal);
        Assert.Contains("copyright: \"MIT\"", configuration, StringComparison.Ordinal);
        Assert.EndsWith("  - \"Reference\": \"reference/Library.md\"", configuration.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>A repository's own docs/mkdocs.yml keeps its pages and navigation, and the site wears the SDK's theme whatever theme it names.</summary>
    [Fact]
    public void SnailSite_WithAConfigurationOfItsOwn_KeepsItAndWearsTheTheme()
    {
        var folder = Library(string.Empty);
        Write(folder, "docs/mkdocs.yml", "site_name: Own\ndocs_dir: content\ntheme:\n  name: null\n  custom_dir: theme\nnav:\n  - Start: index.md\n");
        Write(folder, "docs/content/index.md", "# Own\n\nIts own page.\n");

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite");
        var site = Path.Combine(folder, "obj", "snail-site", "site");

        Assert.True(exitCode == 0, output);
        Assert.Contains("Its own page.", File.ReadAllText(Path.Combine(site, "index.html")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(site, "css", "site.css")));
    }

    /// <summary>A README that opens with a second-level heading still gives the home page its title: the theme sets lower headings in the margin.</summary>
    [Fact]
    public void SnailSite_OfAReadmeOpeningWithASecondLevelHeading_TitlesTheHomePage()
    {
        var folder = Library(string.Empty);
        Write(folder, "README.md", "## Sample\n\nSays hello.\n\n## Usage\n\nCall it.\n");

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite");
        var home = File.ReadAllText(Path.Combine(folder, "obj", "snail-site", "site", "index.html"));

        Assert.True(exitCode == 0, output);
        Assert.Contains("<h1 id=\"sample\">Sample", home, StringComparison.Ordinal);
        Assert.Contains("<h2 id=\"usage\">Usage", home, StringComparison.Ordinal);
    }

    /// <summary>A link to a page the site does not have fails the build rather than publishing a dead link.</summary>
    [Fact]
    public void SnailSite_WithABrokenLink_Fails()
    {
        var folder = Library(string.Empty);
        Write(folder, "docs/guide.md", "# Guide\n\nSee [nothing](missing.md).\n");

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("missing.md", output, StringComparison.Ordinal);
    }

    /// <summary>A folder named with a trailing separator is the same folder: the separator never reaches a command line.</summary>
    [Fact]
    public void SnailSite_IntoAFolderNamedWithATrailingSeparator_WritesThere()
    {
        var folder = Library(string.Empty);
        var output = Path.Combine(folder, "public") + Path.DirectorySeparatorChar;

        var (exitCode, log) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite", $"-p:SnailSiteFolder={output}");

        Assert.True(exitCode == 0, log);
        Assert.True(File.Exists(Path.Combine(output, "index.html")), log);
    }

    /// <summary>An environment whose interpreter is gone — the Python it was made from was upgraded — is made again.</summary>
    [Fact]
    public void SnailSite_WhoseEnvironmentLostItsInterpreter_MakesItAgain()
    {
        var folder = Library(string.Empty);
        var (madeExitCode, madeOutput) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite");

        foreach (var interpreter in Directory.EnumerateFiles(Path.Combine(folder, "obj", "snail-site", "venv", "bin"), "python*"))
            File.Delete(interpreter);

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite");

        Assert.True(madeExitCode == 0, madeOutput);
        Assert.True(exitCode == 0, output);
    }

    /// <summary>An interpreter that is not there is named, with what to do, rather than failing somewhere inside a command.</summary>
    [Fact]
    public void SnailSite_WithAnInterpreterThatIsNotThere_SaysSo()
    {
        var folder = Library(string.Empty);
        var missing = Path.Combine(folder, "no-python");

        var (namedExitCode, named) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite", $"-p:SnailSitePython={missing}");
        var (baseExitCode, withoutPython) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite", $"-p:_SnailSiteBasePython={missing}");

        Assert.NotEqual(0, namedExitCode);
        Assert.Contains($"SnailSitePython names {missing}, which does not exist", named, StringComparison.Ordinal);
        Assert.NotEqual(0, baseExitCode);
        Assert.Contains("install Python 3 with its venv module, or set SnailSitePython", withoutPython, StringComparison.Ordinal);
    }

    /// <summary>A pip that cannot install the pinned MkDocs fails the build saying so, and the next build tries again.</summary>
    [Fact]
    public void SnailSite_WhenPipFails_SaysSoAndTriesAgainNextTime()
    {
        var folder = Library(string.Empty);
        var requirements = Path.Combine(folder, "requirements.txt");

        File.WriteAllText(requirements, "snail-sdk-no-such-package==0.0.0\n");
        var (failedExitCode, failed) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite", $"-p:_SnailSiteRequirements={requirements}");
        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailSite");

        Assert.NotEqual(0, failedExitCode);
        Assert.Contains("pip could not install the packages of", failed, StringComparison.Ordinal);
        Assert.True(exitCode == 0, output);
    }

    /// <summary>The theme's folder is a public property, for a server whose own HTTP pages wear the same stylesheet.</summary>
    [Fact]
    public void SnailSiteTheme_NamesTheFolderOfTheTheme()
    {
        var folder = Library(string.Empty);

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:SnailSiteTheme");

        Assert.True(exitCode == 0, output);
        Assert.True(File.Exists(Path.Combine(output.Trim(), "css", "site.css")), output);
    }

    private string Library(string projectBody) =>
        sdk.Project("Snail.Sdk.Library", "Library", projectBody,
            ("Greeting.cs", Greeting),
            ("PublicAPI.Shipped.txt", PublicApi),
            ("PublicAPI.Unshipped.txt", "#nullable enable\n"));

    private static void Write(string folder, string path, string text)
    {
        var file = Path.Combine(folder, path);

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }
}

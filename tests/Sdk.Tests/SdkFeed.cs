using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Snail.Sdk.Sdk.Tests;

/// <summary>Snail.Sdk packed once per test run into a feed of its own, and projects built against it.</summary>
/// <remarks>
/// The SDK is only what a build does with it, so it is tested by building: each sample is a real project in a folder of
/// its own, with a global.json naming the packed version and a nuget.config naming the feed. The version is the next
/// release's (VersionPrefix) with the run's moment as its prerelease part: it sorts above every release, so package
/// validation against the last one holds, and no package cache answers with an earlier build of the SDK. What the
/// builds restored of it is taken out of the package cache again when the run ends. One feed serves every test class:
/// packing writes into the repository's own bin and obj, so two packs at once would fight over the same files.
/// </remarks>
public sealed class SdkFeed : IDisposable
{
    /// <summary>The collection every test class that builds on the packed SDK belongs to.</summary>
    public const string Collection = "Snail.Sdk packed";

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private readonly string _root = TestRoot.Create();

    private static readonly string[] Packed =
    [
        "src/Sdk/Sdk.csproj", "src/Analyzers/Analyzers.csproj", "src/Library/Library.csproj",
        "src/Mcp/Mcp.csproj", "src/Mcp.Hosting/Mcp.Hosting.csproj", "src/Mcp.Http/Mcp.Http.csproj", "src/Mcp.Testing/Mcp.Testing.csproj",
        "src/Templates/Templates.csproj"
    ];

    /// <summary>Packs the SDK of this repository, the analyzers it brings, its library and server levels and the server libraries.</summary>
    public SdkFeed()
    {
        Version = $"{NextRelease()}-test.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        Feed = Directory.CreateDirectory(Path.Combine(_root, "feed")).FullName;

        foreach (var project in Packed)
        {
            var packed = Dotnet(RepositoryRoot(), "pack", project, "-c", "Release", $"-p:Version={Version}", "-o", Feed);

            Assert.True(packed.ExitCode == 0, packed.Output);
        }
    }

    /// <summary>The version the SDK was packed as.</summary>
    public string Version { get; }

    /// <summary>The folder holding the package.</summary>
    public string Feed { get; }

    /// <summary>A project in a folder of its own, built on the packed SDK.</summary>
    public string Project(string name, string projectBody, params (string Path, string Text)[] files) =>
        Project("Snail.Sdk", name, projectBody, files);

    /// <summary>A project in a folder of its own, built on one level of the packed SDK.</summary>
    public string Project(string sdkName, string name, string projectBody, params (string Path, string Text)[] files)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, $"{name}-{Guid.NewGuid():N}")).FullName;

        File.WriteAllText(Path.Combine(folder, "global.json"), $$"""
            {
              "sdk": { "version": "10.0.302", "rollForward": "latestPatch" },
              "msbuild-sdks": { "Snail.Sdk": "{{Version}}", "Snail.Sdk.Library": "{{Version}}", "Snail.Sdk.Mcp": "{{Version}}" }
            }
            """);
        UseFeed(folder);
        File.WriteAllText(Path.Combine(folder, $"{name}.csproj"), $"""
            <Project Sdk="{sdkName}">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              {projectBody}
            </Project>
            """);

        foreach (var (path, text) in files)
            File.WriteAllText(Path.Combine(folder, path), text);

        return folder;
    }

    /// <summary>A folder whose builds take Snail.Sdk from this feed and everything else from nuget.org.</summary>
    public void UseFeed(string folder) =>
        File.WriteAllText(Path.Combine(folder, "nuget.config"), $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="snail-sdk" value="{Feed}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="nuget.org">
                  <package pattern="*" />
                </packageSource>
                <packageSource key="snail-sdk">
                  <package pattern="Snail.Sdk" />
                  <package pattern="Snail.Sdk.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """);

    /// <summary>A project made from one of the packed templates, in a folder named as it is, building on this feed.</summary>
    public string FromTemplate(string template, string name, params string[] options)
    {
        var hive = Path.Combine(_root, "templates");
        var folder = Path.Combine(_root, $"{Guid.NewGuid():N}", name);
        var package = Path.Combine(Feed, $"Snail.Sdk.Templates.{Version}.nupkg");

        if (!Directory.Exists(hive))
            Assert.Equal(0, Dotnet(_root, "new", "install", package, "--debug:custom-hive", hive).ExitCode);

        var (exitCode, output) = Dotnet(_root, ["new", template, "-n", name, "-o", folder, "--debug:custom-hive", hive, .. options]);

        Assert.True(exitCode == 0, output);
        UseFeed(folder);

        return folder;
    }

    /// <summary>Runs dotnet in a folder and answers with its exit code and everything it wrote.</summary>
    public static (int ExitCode, string Output) Dotnet(string folder, params string[] arguments) =>
        Dotnet(folder, new Dictionary<string, string>(StringComparer.Ordinal), arguments);

    /// <summary>Runs dotnet in a folder with variables of its own and answers with its exit code and everything it wrote.</summary>
    public static (int ExitCode, string Output) Dotnet(string folder, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        using var dotnet = Process.Start(start)!;
        var output = new StringBuilder();
        var errors = new StringBuilder();

        dotnet.OutputDataReceived += (_, line) => output.AppendLine(line.Data);
        dotnet.ErrorDataReceived += (_, line) => errors.AppendLine(line.Data);
        dotnet.BeginOutputReadLine();
        dotnet.BeginErrorReadLine();

        if (!dotnet.WaitForExit(Patience))
        {
            dotnet.Kill(entireProcessTree: true);

            return (-1, "dotnet did not finish in time");
        }

        dotnet.WaitForExit();

        return (dotnet.ExitCode, $"{output}{errors}");
    }

    /// <summary>The rule ids and levels a build reported in its SARIF log.</summary>
    public static IReadOnlyList<(string Rule, string Level)> Reported(string sarifPath)
    {
        using var sarif = JsonDocument.Parse(File.ReadAllText(sarifPath));

        return
        [
            .. sarif.RootElement.GetProperty("runs").EnumerateArray()
                .SelectMany(run => run.TryGetProperty("results", out var results) ? results.EnumerateArray() : Enumerable.Empty<JsonElement>())
                .Select(result => (result.GetProperty("ruleId").GetString()!, result.TryGetProperty("level", out var level) ? level.GetString()! : "warning"))
        ];
    }

    /// <summary>The feed, and what the builds restored of this run's packages into the package cache.</summary>
    public void Dispose()
    {
        foreach (var project in Packed)
        {
            var restored = Path.Combine(PackageCache(), PackageIdOf(project).ToLowerInvariant(), Version.ToLowerInvariant());

            if (Directory.Exists(restored))
                Directory.Delete(restored, recursive: true);
        }

        Directory.Delete(_root, recursive: true);
    }

    /// <summary>The version the repository releases next: VersionPrefix of its Directory.Build.props.</summary>
    private static string NextRelease()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        var prefix = props.Descendants("VersionPrefix").Select(element => element.Value.Trim()).FirstOrDefault();

        return prefix ?? throw new InvalidOperationException("Directory.Build.props sets no VersionPrefix.");
    }

    /// <summary>The package id a packed project gets: Snail.Sdk for src/Sdk, Snail.Sdk.&lt;folder&gt; for the others.</summary>
    private static string PackageIdOf(string project)
    {
        var folder = Path.GetFileName(Path.GetDirectoryName(project))!;

        return folder == "Sdk" ? "Snail.Sdk" : $"Snail.Sdk.{folder}";
    }

    /// <summary>The global packages folder the builds of these tests restore into.</summary>
    private static string PackageCache()
    {
        var configured = Environment.GetEnvironmentVariable("NUGET_PACKAGES");

        return string.IsNullOrEmpty(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
            : configured;
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Snail.Sdk.slnx")))
            folder = folder.Parent;

        return folder?.FullName ?? throw new InvalidOperationException("Snail.Sdk.slnx was not found above the tests.");
    }
}

/// <summary>Binds the feed to its collection, so it is packed once per run.</summary>
[CollectionDefinition(SdkFeed.Collection)]
public sealed class SdkFeedCollection : ICollectionFixture<SdkFeed>;

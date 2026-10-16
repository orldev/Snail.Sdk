using Snail.Sdk.Conformance;

namespace Snail.Sdk.Sdk.Tests;

/// <summary>The checks on repositories that do not keep the standard: each says what it saw.</summary>
public sealed class ConformanceTests : IDisposable
{
    private readonly string _root = TestRoot.Create();

    [Fact]
    public void Repository_WithAnOldSolutionAndNoGlobalJson_FailsBoth()
    {
        var repository = Write("Snail.Toolkit.Old", ("Snail.Toolkit.Old.sln", string.Empty), ("src/Old.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />"));

        var findings = Checks.Of(repository, Standard.Embedded());

        Assert.Contains(findings, finding => finding is { Check: "one .slnx", IsMet: false });
        Assert.Contains(findings, finding => finding is { Check: "global.json", IsMet: false });
        Assert.Contains(findings, finding => finding is { Check: "on Snail.Sdk", IsMet: false, Detail: "not on a Snail.Sdk level: Old.csproj" });
        Assert.False(Report.IsConforming(findings));
    }

    /// <summary>A checkout that turned LF into CRLF keeps the shared file; one that changed a line does not, and says where.</summary>
    [Fact]
    public void EditorConfig_CheckedOutWithCrlf_Holds_AndOneChanged_SaysFromWhichLine()
    {
        var standard = Standard.Embedded();
        var crlf = Write("Snail.Toolkit.Crlf", (".editorconfig", standard.EditorConfig.ReplaceLineEndings("\r\n")));
        var changed = Write("Snail.Toolkit.Changed", (".editorconfig", standard.EditorConfig.Replace("indent_size = 4", "indent_size = 2", StringComparison.Ordinal)));

        var held = Assert.Single(Checks.Of(crlf, standard), found => found.Check == ".editorconfig");
        var differs = Assert.Single(Checks.Of(changed, standard), found => found.Check == ".editorconfig");

        Assert.True(held.IsMet, held.Detail);
        Assert.False(differs.IsMet);
        Assert.StartsWith("differs from the shared file from line ", differs.Detail, StringComparison.Ordinal);
    }

    /// <summary>global.json's sdk section is compared by value: another order, spacing and CRLF hold; another version says only what differs.</summary>
    [Fact]
    public void GlobalJson_ComparedByValue()
    {
        var standard = Standard.Embedded();
        var properties = standard.Sdk.EnumerateObject().Reverse().Select(property => $"\"{property.Name}\" :  {property.Value.GetRawText()}");
        var reordered = Write("Snail.Toolkit.Reordered", ("global.json", $"{{\r\n  \"sdk\": {{ {string.Join(",\r\n", properties)} }}\r\n}}\r\n"));
        var older = Write("Snail.Toolkit.Older", ("global.json", """{ "sdk": { "version": "8.0.100", "rollForward": "latestPatch", "allowPrerelease": false } }"""));

        var held = Assert.Single(Checks.Of(reordered, standard), found => found.Check == "global.json");
        var differs = Assert.Single(Checks.Of(older, standard), found => found.Check == "global.json");

        Assert.True(held.IsMet, held.Detail);
        Assert.False(differs.IsMet);
        Assert.Equal($"sdk.version is \"8.0.100\", the standard's \"{standard.Sdk.GetProperty("version").GetString()}\"", differs.Detail);
    }

    [Fact]
    public void Project_OnTheRazorSdk_WithSnailSdkMcpAsAnElement_IsOnSnailSdk()
    {
        var repository = Write("Snail.MCP.Razor", ("src/Snail.MCP.Razor.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <Sdk Name="Snail.Sdk.Mcp" />
            </Project>
            """));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "on Snail.Sdk");

        Assert.True(finding.IsMet);
    }

    [Fact]
    public void Reference_OfAServerWhoseDocsAreASite_IsFoundWhereItsPagesAre()
    {
        var repository = Write("Snail.MCP.Site", (".mcp/server.json", """{ "packages": [ { "identifier": "Snail.MCP.Site" } ] }"""),
            ("docs/content/schemas/tools.json", "[]"), ("docs/content/tools/index.md", "# Tools"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "reference generated");

        Assert.True(finding.IsMet);
    }

    [Fact]
    public void Site_WithAThemeOfItsOwn_Fails()
    {
        var repository = Write("Snail.MCP.Themed", ("docs/theme/main.html", "<html></html>"), ("docs/mkdocs.yml", "site_name: Themed\ntheme:\n  name: null\n"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "site theme");

        Assert.False(finding.IsMet);
        Assert.Equal("the theme and MkDocs come with the SDK, yet the repository keeps docs/theme, a theme in docs/mkdocs.yml", finding.Detail);
    }

    [Fact]
    public void Library_ThatSaysWhyItIsNotNative_NeedsNoProbe()
    {
        var repository = Write("Snail.Toolkit.Minio", ("src/Snail.Toolkit.Minio.csproj", """
            <Project Sdk="Snail.Sdk.Library">
              <PropertyGroup>
                <SnailNotNativeBecause>Minio 7 reads its answers wrong under Native AOT</SnailNotNativeBecause>
              </PropertyGroup>
            </Project>
            """));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "native probe");

        Assert.True(finding.IsMet);
        Assert.Equal("not native: Minio 7 reads its answers wrong under Native AOT", finding.Detail);
    }

    /// <summary>A repository tool whose kind is set in props, as Snail.Sdk allows, makes no package and owes no public API.</summary>
    [Fact]
    public void Project_WhoseKindIsSetInProps_OwesNoPublicApi()
    {
        var repository = Write("Snail.Toolkit.Tooled",
            ("src/Snail.Toolkit.Tooled.csproj", "<Project Sdk=\"Snail.Sdk.Library\" />"),
            ("src/PublicAPI.Shipped.txt", "#nullable enable\n"),
            ("tools/Directory.Build.props", "<Project><PropertyGroup><SnailProjectKind>Benchmarks</SnailProjectKind></PropertyGroup></Project>"),
            ("tools/Generator/Generator.csproj", "<Project Sdk=\"Snail.Sdk\" />"),
            ("tools/Unpacked/Unpacked.csproj", "<Project Sdk=\"Snail.Sdk\"><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "public API written down");

        Assert.True(finding.IsMet, finding.Detail);
    }

    /// <summary>A library released by a workflow of its own, in place of publish.yml, still keeps the shared workflows.</summary>
    [Fact]
    public void Library_ReleasedByItsOwnWorkflow_KeepsTheSharedWorkflows()
    {
        var repository = Write("Snail.Toolkit.Own",
            (".github/workflows/ci.yml", "uses: orldev/Snail.Sdk/.github/workflows/lib-ci.yml@v1"),
            (".github/workflows/docs.yml", "uses: orldev/Snail.Sdk/.github/workflows/docs.yml@v1"),
            (".github/workflows/perf.yml", "uses: orldev/Snail.Sdk/.github/workflows/perf.yml@v1"),
            (".github/workflows/push_nuget_gallery.yml", "run: dotnet nuget push *.nupkg"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "shared workflows");

        Assert.True(finding.IsMet, finding.Detail);
        Assert.Equal("released by its own push_nuget_gallery.yml", finding.Detail);
    }

    /// <summary>A server built as a container keeps a Dockerfile and builds its image through the shared workflow.</summary>
    [Fact]
    public void Server_BuiltAsAContainer_BuildsItsImageThroughTheSharedWorkflow()
    {
        var project = "<Project Sdk=\"Snail.Sdk.Mcp\"><PropertyGroup><SnailMcpContainer>true</SnailMcpContainer></PropertyGroup></Project>";
        var unbuilt = Write("Snail.MCP.Boxed", ("src/Snail.MCP.Boxed.csproj", project), ("Dockerfile", "FROM scratch"));
        var built = Write("Snail.MCP.Shipped", ("src/Snail.MCP.Shipped.csproj", project), ("Dockerfile", "FROM scratch"),
            (".github/workflows/image.yml", "uses: orldev/Snail.Sdk/.github/workflows/mcp-image.yml@v1"));

        Assert.Single(Checks.Of(unbuilt, Standard.Embedded()), found => found is { Check: "container image", IsMet: false });
        Assert.Single(Checks.Of(built, Standard.Embedded()), found => found is { Check: "container image", IsMet: true });
    }

    [Fact]
    public void ServerManifest_ThatKeepsTheRules_Holds()
    {
        var repository = Write("Snail.MCP.Foo", (".mcp/server.json", Manifest("Snail.MCP.Foo", "stdio", "SNAIL_MCP_FOO_HOME")),
            ("src/Snail.MCP.Foo.csproj", "<Project Sdk=\"Snail.Sdk.Mcp\" />"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "server.json");

        Assert.True(finding.IsMet, finding.Detail);
    }

    [Fact]
    public void ServerManifest_WithAVariableOutsideThePrefix_Fails()
    {
        var repository = Write("Snail.MCP.Foo", (".mcp/server.json", Manifest("Snail.MCP.Foo", "stdio", "FOO_HOME")));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "server.json");

        Assert.Equal(RepositoryKind.Server, repository.Kind);
        Assert.Equal("variables without SNAIL_MCP_FOO_: FOO_HOME", finding.Detail);
    }

    /// <summary>Whatever server.json holds, the check answers with a finding: no packages, no name, values of the wrong type.</summary>
    [Theory]
    [InlineData("""{ "name": "io.github.orldev/snail-mcp-foo" }""", "no packages")]
    [InlineData("""{ "packages": [] }""", "no name")]
    [InlineData("""{ "name": 5, "version": [], "packages": "Snail.MCP.Foo" }""", "no version")]
    [InlineData("""{ "packages": [ 1 ] }""", "a package that is not an object")]
    [InlineData("""{ "packages": [ { "identifier": "Snail.MCP.Foo", "environmentVariables": [ 1, { "name": 2 } ] } ] }""", "variables without SNAIL_MCP_FOO_: a variable without a name, a variable without a name")]
    [InlineData("""{ "description": "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789x", "packages": [] }""", "description is 101 characters, the registry takes 100")]
    [InlineData("[]", ".mcp/server.json is not a JSON object")]
    [InlineData("{ not json", ".mcp/server.json is not a JSON object")]
    public void ServerManifest_OfAnyShape_IsAFinding(string manifest, string seen)
    {
        var repository = Write("Snail.MCP.Foo", (".mcp/server.json", manifest));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "server.json");

        Assert.False(finding.IsMet);
        Assert.Contains(seen, finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ServerManifest_OfAnUnreleasedVersionAnOldSchemaAndAnotherName_SaysEach()
    {
        var manifest = Manifest("Snail.MCP.Foo", "stdio")
            .Replace(Checks.ServerSchema, "https://modelcontextprotocol.io/schemas/draft/2025-07-09/server.json", StringComparison.Ordinal)
            .Replace("\"version\": \"0.1.0\"", "\"version\": \"0.0.0-dev\"", StringComparison.Ordinal)
            .Replace("snail-mcp-foo", "snail-mcp-bar", StringComparison.Ordinal);
        var repository = Write("Snail.MCP.Foo", (".mcp/server.json", manifest));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "server.json");

        Assert.Contains($"$schema is https://modelcontextprotocol.io/schemas/draft/2025-07-09/server.json, not {Checks.ServerSchema}", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("name io.github.orldev/snail-mcp-bar is not <reverse DNS>/<server> ending in -foo", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("version is \"0.0.0-dev\"", finding.Detail, StringComparison.Ordinal);
    }

    /// <summary>A server built for both transports declares both: stdio, and streamable HTTP.</summary>
    [Fact]
    public void ServerManifest_OfAServerOnBothTransports_DeclaresBoth()
    {
        var project = "<Project Sdk=\"Snail.Sdk.Mcp\"><PropertyGroup><SnailMcpTransport>Both</SnailMcpTransport></PropertyGroup></Project>";
        var stdio = Write("Snail.MCP.Foo", (".mcp/server.json", Manifest("Snail.MCP.Foo", "stdio")), ("src/Snail.MCP.Foo.csproj", project));
        var both = Write("Snail.MCP.Bar", (".mcp/server.json", Manifest("Snail.MCP.Bar", "stdio", transports: ["stdio", "streamable-http"])), ("src/Snail.MCP.Bar.csproj", project));

        var once = Assert.Single(Checks.Of(stdio, Standard.Embedded()), found => found.Check == "server.json");
        var twice = Assert.Single(Checks.Of(both, Standard.Embedded()), found => found.Check == "server.json");

        Assert.Equal("transports stdio, but SnailMcpTransport asks for stdio, streamable-http", once.Detail);
        Assert.True(twice.IsMet, twice.Detail);
    }

    [Fact]
    public void DataDirectory_StillUnderTheSnailName_IsAdviceOnly()
    {
        var repository = Write("Snail.MCP.Foo", (".mcp/server.json", """{ "packages": [ { "identifier": "Snail.MCP.Foo" } ] }"""),
            ("src/ServerPaths.cs", "static class ServerPaths { public const string DataDirectoryName = \".snail-mcp-foo\"; }"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "data directory");

        Assert.Equal(Severity.Advice, finding.Severity);
        Assert.False(finding.IsMet);
    }

    private static string Job(string variables) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>s3l.mcp.foo</string>
            <key>EnvironmentVariables</key>
            <dict>{variables}</dict>
        </dict>
        </plist>
        """;

    /// <summary>A token in the job template would travel with the repository and stop every install from restarting the job.</summary>
    [Fact]
    public void JobSecrets_ATokenInTheJobTemplate_FailsTheBuild()
    {
        var repository = Write("Snail.MCP.Foo", ("deploy/s3l.mcp.foo.plist", Job("<key>SNAIL_MCP_FOO_TRANSPORT</key><string>Http</string><key>SNAIL_MCP_FOO_ACCESS_TOKEN</key><string>figd_x</string>")));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "job secrets");

        Assert.Equal(Severity.Required, finding.Severity);
        Assert.False(finding.IsMet);
        Assert.Contains("SNAIL_MCP_FOO_ACCESS_TOKEN", finding.Detail, StringComparison.Ordinal);
        Assert.Contains("~/.config/s3l-mcp-foo/config.json", finding.Detail, StringComparison.Ordinal);
    }

    /// <summary>The transport and the like are the template's to name; a server without a job holds too.</summary>
    [Fact]
    public void JobSecrets_SettingsEveryMachineShares_OrNoJob_Hold()
    {
        var shared = Write("Snail.MCP.Foo", ("deploy/s3l.mcp.foo.plist", Job("<key>SNAIL_MCP_FOO_TRANSPORT</key><string>Http</string><key>SNAIL_MCP_FOO_KEYBOARD_LAYOUT</key><string>us</string>")));
        var none = Write("Snail.MCP.Bar", ("src/Tools.cs", "static class Tools { }"));

        Assert.Single(Checks.Of(shared, Standard.Embedded()), found => found is { Check: "job secrets", IsMet: true });
        Assert.Single(Checks.Of(none, Standard.Embedded()), found => found is { Check: "job secrets", IsMet: true });
    }

    /// <summary>A server named through ServerFolders holds, and so does one that keeps no folder in the home at all.</summary>
    [Fact]
    public void DataDirectory_ThroughServerFolders_OrNone_Holds()
    {
        var named = Write("Snail.MCP.Foo", ("src/ServerPaths.cs", "static class ServerPaths { static string Data => ServerFolders.DataDirectory(\"foo\"); }"));
        var none = Write("Snail.MCP.Bar", ("src/Tools.cs", "static class Tools { }"));

        Assert.Single(Checks.Of(named, Standard.Embedded()), found => found is { Check: "data directory", IsMet: true });
        Assert.Single(Checks.Of(none, Standard.Embedded()), found => found is { Check: "data directory", IsMet: true });
    }

    /// <summary>A library whose PublicAPI.Shipped.txt lists an API is released, though the checkout holds no tag.</summary>
    [Fact]
    public void PackageBaseline_OfALibraryWithAShippedApi_IsAsked()
    {
        var repository = Write("Snail.Toolkit.Shipped", ("src/Snail.Toolkit.Shipped.csproj", "<Project Sdk=\"Snail.Sdk.Library\" />"),
            ("src/PublicAPI.Shipped.txt", "#nullable enable\nSnail.Toolkit.Shipped.Greeting\n"));
        var unreleased = Write("Snail.Toolkit.New", ("src/Snail.Toolkit.New.csproj", "<Project Sdk=\"Snail.Sdk.Library\" />"),
            ("src/PublicAPI.Shipped.txt", "#nullable enable\n"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "package baseline");
        var none = Assert.Single(Checks.Of(unreleased, Standard.Embedded()), found => found.Check == "package baseline");

        Assert.False(finding.IsMet);
        Assert.Equal("released (the shipped API of Snail.Toolkit.Shipped.csproj is written down), but SnailPackageBaseline names no release to validate against", finding.Detail);
        Assert.True(none.IsMet);
    }

    /// <summary>A worktree's .git is a file: its tags are read from the common git folder it points to.</summary>
    [Fact]
    public void PackageBaseline_OfAWorktree_ReadsTheTagsOfItsRepository()
    {
        var common = Directory.CreateDirectory(Path.Combine(_root, "main.git")).FullName;
        var worktree = Directory.CreateDirectory(Path.Combine(common, "worktrees", "Snail.Toolkit.Tagged")).FullName;

        Directory.CreateDirectory(Path.Combine(common, "refs", "tags"));
        File.WriteAllText(Path.Combine(common, "packed-refs"), "# pack-refs with: peeled\n0123 refs/tags/v1.0.0\n");
        File.WriteAllText(Path.Combine(worktree, "commondir"), "../..\n");

        var repository = Write("Snail.Toolkit.Tagged", (".git", $"gitdir: {worktree}\n"), ("src/Snail.Toolkit.Tagged.csproj", "<Project Sdk=\"Snail.Sdk.Library\" />"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "package baseline");

        Assert.Equal("v1.0.0", Assert.Single(repository.Tags()));
        Assert.False(finding.IsMet);
        Assert.StartsWith("released (tagged v1.0.0)", finding.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_WithoutTestsBaselineReadmeOrDependabot_FailsEach()
    {
        var repository = Write("Snail.Toolkit.Bare", ("benchmarks/Snail.Toolkit.Bare.Benchmarks.csproj", "<Project Sdk=\"Snail.Sdk\" />"),
            ("README.md", "## Bare\n"));

        var findings = Checks.Of(repository, Standard.Embedded());

        Assert.Contains(findings, finding => finding is { Id: "S3LC0008", Check: "tests", IsMet: false, Detail: "no *.Tests project" });
        Assert.Contains(findings, finding => finding is { Check: "benchmarks", IsMet: true });
        Assert.Contains(findings, finding => finding is { Check: "benchmark baseline", IsMet: false } && finding.Detail.StartsWith("no *-report.csv in benchmarks/baseline", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding is { Check: "README", IsMet: false, Detail: "README.md opens with \"## Bare\", not an H1 (# Title)" });
        Assert.Contains(findings, finding => finding is { Check: "dependabot", IsMet: false, Detail: "no .github/dependabot.yml" });
    }

    [Fact]
    public void Repository_WithTestsBaselineReadmeAndDependabot_HoldsEach()
    {
        var repository = Write("Snail.Toolkit.Kept", ("benchmarks/Snail.Toolkit.Kept.Benchmarks.csproj", "<Project Sdk=\"Snail.Sdk\" />"),
            ("benchmarks/baseline/Kept.GreetingBenchmarks-report.csv", "Method;Allocated\n"),
            ("tests/Snail.Toolkit.Kept.Tests.csproj", "<Project Sdk=\"Snail.Sdk\" />"),
            ("README.md", "\r\n# Snail.Toolkit.Kept\r\n"), (".github/dependabot.yml", Dependabot));

        var findings = Checks.Of(repository, Standard.Embedded());

        foreach (var check in new[] { "tests", "benchmark baseline", "README", "dependabot" })
            Assert.Contains(findings, finding => finding.Check == check && finding.IsMet);
    }

    /// <summary>A repository tool (SnailProjectKind Tool) is not a benchmark, wherever its kind is set.</summary>
    [Fact]
    public void Benchmarks_DoNotCountATool()
    {
        var repository = Write("Snail.Toolkit.Tools", ("tools/Directory.Build.props", "<Project><PropertyGroup><SnailProjectKind>Tool</SnailProjectKind></PropertyGroup></Project>"),
            ("tools/Generator/Generator.csproj", "<Project Sdk=\"Snail.Sdk\" />"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "benchmarks");

        Assert.False(finding.IsMet);
    }

    /// <summary>A native probe that silences a trim or AOT warning fails; the same code in a library that is not native is debt only.</summary>
    [Fact]
    public void NativeWarnings_SilencedInANativeProbe_Fail()
    {
        var probe = "<Project Sdk=\"Snail.Sdk\"><PropertyGroup><NoWarn>$(NoWarn);IL2104;IL3053</NoWarn></PropertyGroup></Project>";
        var native = Write("Snail.Toolkit.Native", ("tests/Snail.Toolkit.Native.NativeProbe/Snail.Toolkit.Native.NativeProbe.csproj", probe),
            ("benchmarks/Snail.Toolkit.Native.Benchmarks.csproj", "<Project Sdk=\"Snail.Sdk\"><PropertyGroup><NoWarn>$(NoWarn);BL0006</NoWarn></PropertyGroup></Project>"));

        var findings = Checks.Of(native, Standard.Embedded());

        Assert.Contains(findings, finding => finding is { Check: "native warnings", IsMet: false, Detail: "tests/Snail.Toolkit.Native.NativeProbe/Snail.Toolkit.Native.NativeProbe.csproj: NoWarn IL2104, NoWarn IL3053" });
        Assert.Contains(findings, finding => finding is { Check: "migration debt", Severity: Severity.Advice, IsMet: false, Detail: "NoWarn in benchmarks/Snail.Toolkit.Native.Benchmarks.csproj: BL0006" });
    }

    /// <summary>A native server that keeps the native publish from failing on a warning fails; a server that is not native owes nothing.</summary>
    [Fact]
    public void NativeWarnings_OfANativeServerKeptFromFailing_Fail()
    {
        var lenient = "<Project Sdk=\"Snail.Sdk.Mcp\"><PropertyGroup><IlcTreatWarningsAsErrors>false</IlcTreatWarningsAsErrors></PropertyGroup></Project>";
        var native = Write("Snail.MCP.Native", ("src/Snail.MCP.Native.csproj", lenient));
        var managed = Write("Snail.MCP.Managed", ("src/Snail.MCP.Managed.csproj", lenient.Replace("<IlcTreat", "<SnailMcpNative>false</SnailMcpNative><IlcTreat", StringComparison.Ordinal)));

        var failed = Assert.Single(Checks.Of(native, Standard.Embedded()), found => found.Check == "native warnings");
        var spared = Assert.Single(Checks.Of(managed, Standard.Embedded()), found => found.Check == "native warnings");

        Assert.Equal("src/Snail.MCP.Native.csproj: IlcTreatWarningsAsErrors false", failed.Detail);
        Assert.True(spared.IsMet);
    }

    /// <summary>A native publish kept from failing passes only while the CI names the warnings it forgives; a NoWarn never does.</summary>
    [Fact]
    public void NativeWarnings_ForgivenByName_InTheCi_AreMet()
    {
        var lenient = "<Project Sdk=\"Snail.Sdk.Mcp\"><PropertyGroup><IlcTreatWarningsAsErrors>false</IlcTreatWarningsAsErrors></PropertyGroup></Project>";
        var ci = "jobs:\n  ci:\n    uses: orldev/Snail.Sdk/.github/workflows/mcp-ci.yml@v1\n    with:\n      forgiven-native-warnings: 'Jint\\.dll|: Jint\\.'\n";
        var forgiven = Write("Snail.MCP.Forgiven", ("src/Snail.MCP.Forgiven.csproj", lenient), (".github/workflows/ci.yml", ci));
        var silenced = Write("Snail.MCP.Silenced", ("src/Snail.MCP.Silenced.csproj", lenient.Replace("<IlcTreat", "<NoWarn>$(NoWarn);IL2026</NoWarn><IlcTreat", StringComparison.Ordinal)), (".github/workflows/ci.yml", ci));

        var met = Assert.Single(Checks.Of(forgiven, Standard.Embedded()), found => found.Check == "native warnings");
        var unmet = Assert.Single(Checks.Of(silenced, Standard.Embedded()), found => found.Check == "native warnings");

        Assert.True(met.IsMet);
        Assert.Equal("the CI forgives only Jint\\.dll|: Jint\\.", met.Detail);
        Assert.False(unmet.IsMet);
    }

    /// <summary>The release feed is asked only on a release, and then fails on everything the local feed left behind.</summary>
    [Fact]
    public void ReleaseFeed_IsAskedOnlyOnARelease_AndFailsOnTheLocalFeed()
    {
        var repository = Write("Snail.Toolkit.Local",
            ("nuget.config", """
                <configuration>
                  <packageSources><add key="LocalPackages" value="../../nuget" /><add key="Bundled" value="./packages" /></packageSources>
                  <packageSourceMapping><packageSource key="LocalPackages"><package pattern="Snail.*" /></packageSource></packageSourceMapping>
                </configuration>
                """),
            ("global.json", """{ "msbuild-sdks": { "Snail.Sdk.Library": "0.1.0-local.23" } }"""),
            ("Directory.Packages.props", """<Project><ItemGroup><PackageVersion Include="Snail.Toolkit.HttpBuilder.Extensions" Version="0.4.0-local" /><PackageVersion Include="xunit" Version="2.9.3" /></ItemGroup></Project>"""));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded(), isRelease: true), found => found.Check == "release feed");

        Assert.DoesNotContain(Checks.Of(repository, Standard.Embedded()), found => found.Check == "release feed");
        Assert.Equal("nuget.config adds the source LocalPackages; nuget.config maps packages to LocalPackages; global.json takes Snail.Sdk.Library 0.1.0-local.23; "
            + "Directory.Packages.props takes Snail.Toolkit.HttpBuilder.Extensions 0.4.0-local", finding.Detail);
    }

    /// <summary>publish.yml releases in two jobs: the shared workflow without a token, then a job of the repository's own pushing with one.</summary>
    [Theory]
    [InlineData("contents: read", "", "publish.yml calls mcp-publish.yml but pushes through no nuget-push job: nuget.org refuses a key to a job of Snail.Sdk's workflows")]
    [InlineData("id-token: write", "contents: read", "the release job release holds id-token: only the push job needs it")]
    [InlineData("contents: read", "contents: read", "the push job push holds no id-token: write, which the nuget-push action signs in with")]
    [InlineData("contents: read", "id-token: write", "")]
    public void SharedWorkflows_PublishThroughTheSharedWorkflowAndAPushJobOfItsOwn(string workflowPermission, string pushPermission, string seen)
    {
        var push = pushPermission.Length == 0 ? string.Empty : $$"""
              push:
                needs: release
                runs-on: ubuntu-latest
                permissions:
                  {{pushPermission}}
                steps:
                  - uses: orldev/Snail.Sdk/.github/actions/nuget-push@v1
                    with:
                      user: devorl
            """;
        var publish = $"""
            on:
              push:
                tags: ['v[0-9]+.[0-9]+.[0-9]+*']
            permissions:
              {workflowPermission}
            jobs:
              release:
                uses: orldev/Snail.Sdk/.github/workflows/mcp-publish.yml@v1
                with:
                  contract-variable: SNAIL_HALF_SERVER
            {push}
            """;
        var repository = Write("Snail.MCP.Half", [.. CallerWorkflows("mcp"), (".github/workflows/publish.yml", publish)]);

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "shared workflows");

        Assert.Equal(seen, finding.Detail);
        Assert.Equal(seen.Length == 0, finding.IsMet);
    }

    /// <summary>The template's own workflows are what the check asks for, inputs and tags included.</summary>
    [Theory]
    [InlineData("s3l-lib", "Snail.Toolkit.Kept")]
    [InlineData("s3l-mcp", "Snail.MCP.Kept")]
    public void Workflows_OfTheTemplates_KeepTheShape(string template, string name)
    {
        var folder = Path.Combine(TemplatesRoot(), template, ".github");
        var files = Directory.EnumerateFiles(folder, "*.yml", SearchOption.AllDirectories)
            .Select(file => ($".github/{Path.GetRelativePath(folder, file).Replace('\\', '/')}", File.ReadAllText(file)))
            .ToArray();
        var repository = Write(name, files);

        var findings = Checks.Of(repository, Standard.Embedded());

        foreach (var check in new[] { "shared workflows", "workflow inputs", "release tag", "dependabot" })
            Assert.Contains(findings, finding => finding.Check == check && finding.IsMet && finding.Detail.Length == 0);
    }

    /// <summary>A call passing an input the shared workflow no longer takes, or leaving out one it requires, fails before GitHub refuses it.</summary>
    [Fact]
    public void WorkflowInputs_RemovedOrMissing_Fail()
    {
        var ci = """
            jobs:
              ci:
                uses: orldev/Snail.Sdk/.github/workflows/mcp-ci.yml@v1
                with:
                  name: Snail.MCP.Old
                  test-os: macos-latest
              image:
                uses: orldev/Snail.Sdk/.github/workflows/gone.yml@v1
            """;
        var repository = Write("Snail.MCP.Old", (".github/workflows/ci.yml", ci));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "workflow inputs");

        Assert.Equal("ci.yml:3 passes name, test-os, which .github/workflows/mcp-ci.yml no longer takes; ci.yml:3 leaves out contract-variable, "
            + "which .github/workflows/mcp-ci.yml requires; ci.yml:8 calls .github/workflows/gone.yml, which Snail.Sdk does not keep", finding.Detail);
    }

    [Theory]
    [InlineData("tags: ['v*']", false)]
    [InlineData("tags:\n      - \"v*\"", false)]
    [InlineData("tags: ['v[0-9]+.[0-9]+.[0-9]+*']", true)]
    public void ReleaseTag_ThatV1Matches_IsAdvisedAgainst(string tags, bool isMet)
    {
        var repository = Write("Snail.Toolkit.Tagged", (".github/workflows/publish.yml", $"on:\n  push:\n    {tags}\n"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "release tag");

        Assert.Equal(Severity.Advice, finding.Severity);
        Assert.Equal(isMet, finding.IsMet);
    }

    [Fact]
    public void Dependabot_WithoutTheNuGetEcosystem_Fails()
    {
        var repository = Write("Snail.Toolkit.Actions", (".github/dependabot.yml", "version: 2\nupdates:\n  - package-ecosystem: github-actions\n"));

        var finding = Assert.Single(Checks.Of(repository, Standard.Embedded()), found => found.Check == "dependabot");

        Assert.Equal(".github/dependabot.yml lacks package-ecosystem: nuget", finding.Detail);
    }

    [Theory]
    [InlineData("Snail.MCP.CleanUp", "cleanup")]
    [InlineData("snail.mcp.Code.Context", "codecontext")]
    [InlineData("Other.Server", "otherserver")]
    public void ShortName_StripsThePrefixInAnyCase_AndTheDots(string name, string shortName) => Assert.Equal(shortName, Repository.ShortNameOf(name));

    /// <summary>Every rule keeps an id of its own, S3LC and four digits, and an unmet one is printed the way MSBuild reads an error.</summary>
    [Fact]
    public void Rules_HaveDistinctIds_AndPrintAsMSBuildMessages()
    {
        Assert.All(Rules.All, rule => Assert.Matches("^S3LC[0-9]{4}$", rule.Id));
        Assert.Equal(Rules.All.Count, Rules.All.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Rules.All.Count, Rules.All.Select(rule => rule.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("error S3LC0001: one .slnx: 2 .slnx, 0 .sln at the root", Report.Canonical(Rules.Solution.Unmet("2 .slnx, 0 .sln at the root")));
        Assert.Equal("warning S3LC0017: migration debt: NoWarn in Directory.Build.props: CS1591", Report.Canonical(Rules.MigrationDebt.Unmet("NoWarn in Directory.Build.props: CS1591")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private const string Dependabot = "version: 2\nupdates:\n  - package-ecosystem: github-actions\n  - package-ecosystem: nuget\n";

    private static (string Path, string Text)[] CallerWorkflows(string level) =>
    [
        (".github/workflows/ci.yml", $"jobs:\n  ci:\n    uses: orldev/Snail.Sdk/.github/workflows/{level}-ci.yml@v1\n"),
        (".github/workflows/docs.yml", "jobs:\n  docs:\n    uses: orldev/Snail.Sdk/.github/workflows/docs.yml@v1\n"),
        (".github/workflows/perf.yml", "jobs:\n  perf:\n    uses: orldev/Snail.Sdk/.github/workflows/perf.yml@v1\n")
    ];

    private static string TemplatesRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Snail.Sdk.slnx")))
            folder = folder.Parent;

        return Path.Combine(folder?.FullName ?? throw new InvalidOperationException("Snail.Sdk.slnx was not found above the tests."), "src", "Templates", "content");
    }

    private static string Manifest(string identifier, string transport, string variable = "SNAIL_MCP_FOO_HOME", string[]? transports = null)
    {
        var shortName = Repository.ShortNameOf(identifier);
        var packages = (transports ?? [transport]).Select(type => $$"""
            { "registryType": "nuget", "identifier": "{{identifier}}", "version": "0.1.0", "transport": { "type": "{{type}}" },
              "environmentVariables": [ { "name": "{{variable.Replace("FOO", shortName.ToUpperInvariant(), StringComparison.Ordinal)}}" } ] }
            """);

        return $$"""
            { "$schema": "{{Checks.ServerSchema}}", "name": "io.github.orldev/snail-mcp-{{shortName}}", "version": "0.1.0",
              "packages": [ {{string.Join(", ", packages)}} ] }
            """;
    }

    /// <summary>A path the way a shell's tab completion writes it, with a trailing separator, still names the repository after its folder.</summary>
    [Fact]
    public void Repository_AtAPathWithATrailingSeparator_IsNamedAfterTheFolder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Snail.MCP.Demo")).FullName;

        var repository = Repository.At(folder + Path.DirectorySeparatorChar);

        Assert.Equal("Snail.MCP.Demo", repository.Name);
        Assert.Equal(RepositoryKind.Server, repository.Kind);
    }

    private Repository Write(string name, params (string Path, string Text)[] files)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

        foreach (var (path, text) in files)
        {
            var file = Path.Combine(folder, path);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        return Repository.At(folder);
    }
}

namespace Snail.Sdk.Sdk.Tests;

/// <summary>What a project built on Snail.Sdk is held to, and what it is only advised.</summary>
[Collection(SdkFeed.Collection)]
public sealed class SdkTests(SdkFeed sdk)
{
    private const string Clean = """
        namespace Clean;

        /// <summary>Says hello.</summary>
        public sealed class Greeting(string name)
        {
            /// <summary>The greeting.</summary>
            public string Text() => $"Hello, {name}";
        }
        """;

    [Fact]
    public void Build_OfCleanCode_SucceedsWithoutAWarning()
    {
        var folder = sdk.Project("Clean", string.Empty, ("Greeting.cs", Clean));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build");

        Assert.True(exitCode == 0, output);
        Assert.Contains("0 Warning(s)", output, StringComparison.Ordinal);
    }

    /// <summary>The rules the guidelines state fail the build: a comparison without its StringComparison, a blocking wait, and a boolean that does not ask (Snail.Sdk.Analyzers).</summary>
    /// <remarks>The sample is otherwise clean, so the failure is those rules and not a namespace or a missing doc.</remarks>
    [Fact]
    public void Build_OfCodeBreakingAnAgreedRule_Fails()
    {
        var folder = sdk.Project("Broken", string.Empty, ("Greeting.cs", """
            namespace Broken;

            /// <summary>Says hello.</summary>
            public sealed class Greeting(string name)
            {
                /// <summary>Whether this greeting is for the name.</summary>
                public bool IsFor(string other) => name.Equals(other);

                /// <summary>The length of the greeting.</summary>
                public int Length() => Task.Run(() => name.Length).Result;

                /// <summary>Whether the greeting is ready.</summary>
                public bool Ready => name.Length > 0;
            }
            """));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("CA1307", output, StringComparison.Ordinal);
        Assert.Contains("VSTHRD002", output, StringComparison.Ordinal);
        Assert.Contains("S3L0008", output, StringComparison.Ordinal);
    }

    /// <summary>A rule the SDK ships disabled is still seen — as advice that leaves the build green.</summary>
    /// <remarks>CA1802 (a static readonly string that could be const) is off by default; a category setting does not reach it.</remarks>
    [Fact]
    public void Build_OfCodeARuleDisabledByDefaultFlags_ReportsItAsAdvice()
    {
        var folder = sdk.Project("Advised", string.Empty, ("Greeting.cs", """
            namespace Advised;

            /// <summary>Says hello.</summary>
            public sealed class Greeting(string name)
            {
                private static readonly string Prefix = "Hello";

                /// <summary>The greeting.</summary>
                public string Text() => $"{Prefix}, {name}";
            }
            """));
        var sarif = Path.Combine(folder, "build.sarif");

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build", $"-p:ErrorLog={sarif}%2Cversion=2.1");

        Assert.True(exitCode == 0, output);
        Assert.Contains(("CA1802", "note"), SdkFeed.Reported(sarif));
    }

    /// <summary>A test project is not packed or documented, and its names read as xUnit's do: underscores, async without the suffix.</summary>
    [Fact]
    public void Project_NamedTests_IsNeitherPackedNorDocumented()
    {
        var folder = sdk.Project("Sample.Tests", string.Empty, ("Greeting.cs", """
            namespace Sample.Tests;

            public sealed class Greeting_Says_Hello
            {
                public string Text() => "Hello";

                public async Task Text_IsHello() => await Task.Yield();
            }
            """));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:IsPackable", "-getProperty:SnailProjectKind");
        var (built, buildOutput) = SdkFeed.Dotnet(folder, "build");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"IsPackable\": \"false\"", output, StringComparison.Ordinal);
        Assert.Contains("\"SnailProjectKind\": \"Tests\"", output, StringComparison.Ordinal);
        Assert.True(built == 0, buildOutput);
    }

    /// <summary>A test project directly in tests/ leaves the native probe beside it, tests/&lt;Name&gt;.NativeProbe/, to that probe.</summary>
    [Fact]
    public void Project_NamedTests_LeavesTheProbeBesideItAlone()
    {
        var folder = sdk.Project("Sample.Tests", string.Empty, ("Greeting.cs", "namespace Sample.Tests;\n\npublic sealed class Greeting;\n"));
        var probe = Directory.CreateDirectory(Path.Combine(folder, "Sample.NativeProbe")).FullName;

        File.WriteAllText(Path.Combine(probe, "Program.cs"), "this is the probe's code, not the tests'");
        var (exitCode, output) = SdkFeed.Dotnet(folder, "build");

        Assert.True(exitCode == 0, output);
    }

    /// <summary>Beside Razor the test project's relaxations reach the compiler too, though the targets come after Razor's.</summary>
    [Fact]
    public void Project_NamedTests_BesideRazor_IsSparedAlike()
    {
        var folder = sdk.Project("Microsoft.NET.Sdk.Razor", "Sample.Tests", """<Sdk Name="Snail.Sdk" />""", ("Greeting.cs", "namespace Sample.Tests;\n\npublic sealed class Greeting;\n"));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getItem:EditorConfigFiles");

        Assert.True(exitCode == 0, output);
        Assert.Contains("Snail.Sdk.Tests.globalconfig", output, StringComparison.Ordinal);
    }

    /// <summary>A probe turns off what a native web app turns off: SignalR's reflection over custom awaitables.</summary>
    [Fact]
    public void Project_NamedNativeProbe_TurnsOffSignalRCustomAwaitables()
    {
        var folder = sdk.Project("Sample.NativeProbe", string.Empty, ("Program.cs", "System.Console.WriteLine(\"probe\");"));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getItem:RuntimeHostConfigurationOption");

        Assert.True(exitCode == 0, output);
        Assert.Contains("Microsoft.AspNetCore.SignalR.Hub.IsCustomAwaitableSupported", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_NamedBenchmarks_IsAProgram()
    {
        var folder = sdk.Project("Sample.Benchmarks", string.Empty, ("Program.cs", """Console.WriteLine("benchmarks");"""));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:OutputType", "-getProperty:IsPackable");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"OutputType\": \"Exe\"", output, StringComparison.Ordinal);
        Assert.Contains("\"IsPackable\": \"false\"", output, StringComparison.Ordinal);
    }

    /// <summary>A program of the repository's own is not packed, and is held to the rules a test or benchmark is spared.</summary>
    [Fact]
    public void Project_OfKindTool_IsAProgramHeldToEveryRule()
    {
        var folder = sdk.Project("Generator", "<PropertyGroup><SnailProjectKind>Tool</SnailProjectKind></PropertyGroup>", ("Program.cs", """Console.WriteLine("generated");"""));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:OutputType", "-getProperty:IsPackable", "-getProperty:NoWarn", "-getItem:GlobalAnalyzerConfigFiles");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"OutputType\": \"Exe\"", output, StringComparison.Ordinal);
        Assert.Contains("\"IsPackable\": \"false\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CS1591", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Snail.Sdk.Tests.globalconfig", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A repository's Directory.Build.props keeps the defaults it changes — the language version, the analyzers' versions —
    /// and does not switch off a rule of the standard for all its projects at once.
    /// </summary>
    [Fact]
    public void DirectoryBuildProps_KeepsItsDefaults_ButNotARuleOfTheStandard()
    {
        var folder = sdk.Project("Clean", string.Empty, ("Greeting.cs", Clean), ("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <LangVersion>preview</LangVersion>
                <ImplicitUsings>disable</ImplicitUsings>
                <SnailThreadingAnalyzersVersion>17.13.61</SnailThreadingAnalyzersVersion>
                <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
                <NuGetAuditLevel>high</NuGetAuditLevel>
              </PropertyGroup>
            </Project>
            """));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild",
            "-getProperty:LangVersion", "-getProperty:ImplicitUsings", "-getProperty:SnailThreadingAnalyzersVersion",
            "-getProperty:TreatWarningsAsErrors", "-getProperty:NuGetAuditLevel");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"LangVersion\": \"preview\"", output, StringComparison.Ordinal);
        Assert.Contains("\"ImplicitUsings\": \"disable\"", output, StringComparison.Ordinal);
        Assert.Contains("\"SnailThreadingAnalyzersVersion\": \"17.13.61\"", output, StringComparison.Ordinal);
        Assert.Contains("\"TreatWarningsAsErrors\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"NuGetAuditLevel\": \"low\"", output, StringComparison.Ordinal);
    }

    /// <summary>The conformance check names the repository by its global.json, and says so when there is none.</summary>
    [Fact]
    public void SnailConformance_OutsideARepository_FailsSayingWhy()
    {
        var folder = sdk.Project($"Snail.Sdk/{sdk.Version}", "Clean", string.Empty, ("Greeting.cs", Clean));

        File.Delete(Path.Combine(folder, "global.json"));
        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:SnailConformance", "-nologo");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("no global.json", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Usage:", output, StringComparison.Ordinal);
    }

    /// <summary>A known vulnerability anywhere in the graph is audited, the build is deterministic and source-linked.</summary>
    [Fact]
    public void Project_OnTheSdk_IsAuditedAndDeterministic()
    {
        var folder = sdk.Project("Clean", string.Empty, ("Greeting.cs", Clean));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild",
            "-getProperty:NuGetAudit", "-getProperty:NuGetAuditMode", "-getProperty:TreatWarningsAsErrors",
            "-getProperty:Deterministic", "-getProperty:AnalysisLevel", "-getProperty:AnalysisMode");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"NuGetAuditMode\": \"all\"", output, StringComparison.Ordinal);
        Assert.Contains("\"TreatWarningsAsErrors\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"AnalysisLevel\": \"latest\"", output, StringComparison.Ordinal);
        Assert.Contains("\"AnalysisMode\": \"Default\"", output, StringComparison.Ordinal);
    }
}

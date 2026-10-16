namespace Snail.Sdk.Sdk.Tests;

/// <summary>What a server built on Snail.Sdk.Mcp gets from three properties, and what its test project gets.</summary>
[Collection(SdkFeed.Collection)]
public sealed class McpSdkTests(SdkFeed sdk)
{
    private const string Program = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Server;
        using Snail.Sdk.Mcp.Hosting;

        var builder = McpHost.CreateBuilder(args);

        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<EchoTools>();

        await builder.Build().RunAsync();
        """;

    private const string Tools = """
        using System.ComponentModel;
        using ModelContextProtocol.Server;

        namespace Server;

        /// <summary>Says things back.</summary>
        [McpServerToolType]
        public sealed class EchoTools
        {
            /// <summary>Answers with the text it was given.</summary>
            [McpServerTool(Name = "echo", ReadOnly = true, Idempotent = true), Description("Answer with the text it was given.")]
            public static string Echo([Description("What to answer with.")] string text) => text;

            /// <summary>Answers with the text in capitals.</summary>
            [McpServerTool(Name = "shout", ReadOnly = true), Description("Answer with the text in capitals.")]
            public string Shout([Description("What to shout.")] string text) => text.ToUpperInvariant();

            /// <summary>Answers with the text and the moment, the clock coming from the container.</summary>
            [McpServerTool(Name = "stamp", ReadOnly = true), Description("Answer with the text and the moment it was asked.")]
            public static string Stamp(TimeProvider clock, Echoes echoes, [Description("What to stamp.")] string text) => $"{clock.GetUtcNow():O} {echoes.Of(text)}";
        }

        /// <summary>A service of the server's own, filled by its container.</summary>
        public sealed class Echoes(TimeProvider clock)
        {
            /// <summary>The text as it comes back.</summary>
            public string Of(string text) => $"{text} ({clock.LocalTimeZone.Id})";
        }
        """;

    private const string FrameworkDependent = """
        <PropertyGroup>
          <SnailMcpNative>false</SnailMcpNative>
          <RootNamespace>Server</RootNamespace>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="ModelContextProtocol" Version="2.2.0" />
        </ItemGroup>
        """;

    [Fact]
    public void Server_IsANativeToolOfTheMcpServerKind_ByDefault()
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", string.Empty);

        var (_, output) = SdkFeed.Dotnet(folder, "msbuild",
            "-getProperty:OutputType", "-getProperty:PackAsTool", "-getProperty:PackageType", "-getProperty:ToolCommandName",
            "-getProperty:PublishAot", "-getProperty:IsAotCompatible", "-getProperty:InvariantGlobalization", "-getProperty:SnailMcpName");

        Assert.Contains("\"OutputType\": \"Exe\"", output, StringComparison.Ordinal);
        Assert.Contains("\"PackAsTool\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"PackageType\": \"McpServer\"", output, StringComparison.Ordinal);
        Assert.Contains("\"ToolCommandName\": \"s3l-mcp-sample\"", output, StringComparison.Ordinal);
        Assert.Contains("\"PublishAot\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"IsAotCompatible\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"InvariantGlobalization\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"SnailMcpName\": \"Snail.MCP.Sample\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_ThatIsNotNative_OrBuildsAnImage_StaysFrameworkDependent()
    {
        var notNative = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", "<PropertyGroup><SnailMcpNative>false</SnailMcpNative></PropertyGroup>");
        var container = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", "<PropertyGroup><SnailMcpContainer>true</SnailMcpContainer></PropertyGroup>");

        var (_, notNativeOutput) = SdkFeed.Dotnet(notNative, "msbuild", "-getProperty:PublishAot", "-getProperty:IsAotCompatible");
        var (_, imageOutput) = SdkFeed.Dotnet(container, "msbuild", "-getProperty:PublishAot");
        var (_, nativeOutput) = SdkFeed.Dotnet(container, "msbuild", "-getProperty:PublishAot", "-p:RuntimeIdentifier=osx-arm64");

        Assert.Contains("\"PublishAot\": \"\"", notNativeOutput, StringComparison.Ordinal);
        Assert.Contains("\"IsAotCompatible\": \"false\"", notNativeOutput, StringComparison.Ordinal);
        Assert.Equal(string.Empty, imageOutput.Trim());
        Assert.Equal("true", nativeOutput.Trim());
    }

    /// <summary>The short name is the name without a leading Snail.MCP. in any case, without dots, in lower case.</summary>
    [Theory]
    [InlineData("Snail.MCP.Code.Context", "s3l-mcp-codecontext")]
    [InlineData("snail.mcp.CleanUp", "s3l-mcp-cleanup")]
    [InlineData("Acme.Snail.MCP.Tool", "s3l-mcp-acmesnailmcptool")]
    public void Server_ShortName_FollowsTheOneRule(string name, string command)
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", name, string.Empty);

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:ToolCommandName");

        Assert.True(exitCode == 0, output);
        Assert.Equal(command, output.Trim());
    }

    /// <summary>What a server is — native, its transport — and its globalization and roll-forward can be set once for a repository.</summary>
    [Fact]
    public void Server_InARepositoryThatDescribesItsServers_KeepsTheDescription()
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", string.Empty, ("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <SnailMcpNative>false</SnailMcpNative>
                <SnailMcpTransport>Both</SnailMcpTransport>
                <SnailMcpContainer>true</SnailMcpContainer>
                <InvariantGlobalization>false</InvariantGlobalization>
                <RollForward>Major</RollForward>
              </PropertyGroup>
            </Project>
            """));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild",
            "-getProperty:SnailMcpNative", "-getProperty:SnailMcpTransport", "-getProperty:SnailMcpContainer",
            "-getProperty:InvariantGlobalization", "-getProperty:RollForward", "-getProperty:PublishAot");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"SnailMcpNative\": \"false\"", output, StringComparison.Ordinal);
        Assert.Contains("\"SnailMcpTransport\": \"Both\"", output, StringComparison.Ordinal);
        Assert.Contains("\"SnailMcpContainer\": \"true\"", output, StringComparison.Ordinal);
        Assert.Contains("\"InvariantGlobalization\": \"false\"", output, StringComparison.Ordinal);
        Assert.Contains("\"RollForward\": \"Major\"", output, StringComparison.Ordinal);
        Assert.Contains("\"PublishAot\": \"\"", output, StringComparison.Ordinal);
    }

    /// <summary>The workstation GC for every server, the Web SDK's included, which turns the server GC on; the project file has the last word.</summary>
    [Fact]
    public void Server_GarbageCollector_IsTheWorkstationOne_UnlessTheProjectAsks()
    {
        var plain = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", string.Empty);
        var web = sdk.Project("Microsoft.NET.Sdk.Web", "Snail.MCP.Sample", $"""
            <Sdk Name="Snail.Sdk.Mcp" />
            {FrameworkDependent}
            """);
        var asked = sdk.Project("Microsoft.NET.Sdk.Web", "Snail.MCP.Sample", $"""
            <Sdk Name="Snail.Sdk.Mcp" />
            {FrameworkDependent}
            <PropertyGroup><ServerGarbageCollection>true</ServerGarbageCollection></PropertyGroup>
            """);

        var (_, plainOutput) = SdkFeed.Dotnet(plain, "msbuild", "-getProperty:ServerGarbageCollection", "-getProperty:ConcurrentGarbageCollection");
        var (_, webOutput) = SdkFeed.Dotnet(web, "msbuild", "-getProperty:ServerGarbageCollection");
        var (_, askedOutput) = SdkFeed.Dotnet(asked, "msbuild", "-getProperty:ServerGarbageCollection");

        Assert.Contains("\"ServerGarbageCollection\": \"false\"", plainOutput, StringComparison.Ordinal);
        Assert.Contains("\"ConcurrentGarbageCollection\": \"\"", plainOutput, StringComparison.Ordinal);
        Assert.Equal("false", webOutput.Trim());
        Assert.Equal("true", askedOutput.Trim());
    }

    /// <summary>A framework-dependent server runs without profile-guided tiering unless its project asks; a native one has no JIT to tier.</summary>
    [Fact]
    public void Server_TieredPgo_IsOffWhenFrameworkDependent_UnlessTheProjectAsks()
    {
        var native = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", string.Empty);
        var dependent = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", FrameworkDependent);
        var asked = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", $"""
            {FrameworkDependent}
            <PropertyGroup><TieredPGO>true</TieredPGO></PropertyGroup>
            """);

        var (_, nativeOutput) = SdkFeed.Dotnet(native, "msbuild", "-getProperty:TieredPGO");
        var (_, dependentOutput) = SdkFeed.Dotnet(dependent, "msbuild", "-getProperty:TieredPGO");
        var (_, askedOutput) = SdkFeed.Dotnet(asked, "msbuild", "-getProperty:TieredPGO");

        Assert.Equal(string.Empty, nativeOutput.Trim());
        Assert.Equal("false", dependentOutput.Trim());
        Assert.Equal("true", askedOutput.Trim());
    }

    /// <summary>SnailMcpInstall asks for the install; Install, the shorter name, asks the same.</summary>
    [Theory]
    [InlineData("-p:SnailMcpInstall=true")]
    [InlineData("-p:Install=true")]
    public void Publish_AskedToInstall_ByEitherName_Installs(string request)
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", string.Empty);

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:_IsSnailMcpInstall", request);

        Assert.True(exitCode == 0, output);
        Assert.Equal("true", output.Trim());
    }

    /// <summary>The command is written into the assembly, where ServerIdentity reads the name the server announces.</summary>
    [Fact]
    public void Server_CarriesItsCommand_AsTheNameItAnnounces()
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", string.Empty);

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-getItem:AssemblyMetadata");

        Assert.True(exitCode == 0, output);
        Assert.Contains("\"Identity\": \"Snail.Sdk.Mcp.ServerName\"", output, StringComparison.Ordinal);
        Assert.Contains("\"Value\": \"s3l-mcp-sample\"", output, StringComparison.Ordinal);
    }

    /// <summary>A server on the Razor or Web SDK keeps it and adds Snail.Sdk.Mcp as an element: one Microsoft.NET.Sdk, both levels.</summary>
    [Fact]
    public void Server_OnTheRazorSdk_TakesSnailSdkMcpAsAnElement()
    {
        var folder = sdk.Project("Microsoft.NET.Sdk.Razor", "Snail.MCP.Sample", $"""
            <Sdk Name="Snail.Sdk.Mcp" />
            {FrameworkDependent}
            """, ("Program.cs", Program), ("EchoTools.cs", Tools));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build");
        var (_, properties) = SdkFeed.Dotnet(folder, "msbuild", "-getProperty:PackageType", "-getProperty:UsingMicrosoftNETSdkRazor", "-getProperty:TreatWarningsAsErrors");
        var runtimeConfig = Directory.EnumerateFiles(Path.Combine(folder, "bin"), "Snail.MCP.Sample.runtimeconfig.json", SearchOption.AllDirectories);

        Assert.True(exitCode == 0, output);
        Assert.NotEmpty(runtimeConfig);
        Assert.DoesNotContain("MSB4011", output, StringComparison.Ordinal);
        Assert.Contains("\"PackageType\": \"McpServer\"", properties, StringComparison.Ordinal);
        Assert.Contains("\"UsingMicrosoftNETSdkRazor\": \"true\"", properties, StringComparison.Ordinal);
        Assert.Contains("\"TreatWarningsAsErrors\": \"true\"", properties, StringComparison.Ordinal);
    }

    [Fact]
    public void Libraries_ComeImplicitly_ByTransportAndKind()
    {
        var stdio = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", string.Empty);
        var http = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", "<PropertyGroup><SnailMcpTransport>Both</SnailMcpTransport></PropertyGroup>");
        var tests = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample.Tests", string.Empty);

        var (_, stdioOutput) = SdkFeed.Dotnet(stdio, "msbuild", "-getItem:PackageReference");
        var (_, httpOutput) = SdkFeed.Dotnet(http, "msbuild", "-getItem:PackageReference");
        var (_, testsOutput) = SdkFeed.Dotnet(tests, "msbuild", "-getItem:PackageReference", "-getProperty:PackAsTool");

        Assert.Contains("Snail.Sdk.Mcp.Hosting", stdioOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Snail.Sdk.Mcp.Http", stdioOutput, StringComparison.Ordinal);
        Assert.Contains("Snail.Sdk.Mcp.Http", httpOutput, StringComparison.Ordinal);
        Assert.Equal("true", SdkFeed.Dotnet(http, "msbuild", "-getProperty:EnableRequestDelegateGenerator").Output.Trim());
        Assert.Contains("Snail.Sdk.Mcp.Testing", testsOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Snail.Sdk.Mcp.Hosting", testsOutput, StringComparison.Ordinal);
        Assert.Contains("\"PackAsTool\": \"\"", testsOutput, StringComparison.Ordinal);
    }

    /// <summary>The reference is what the server's own MCP SDK makes of each tool: the wire copy, with the behaviour in words.</summary>
    [Fact]
    public void Build_WithSnailReference_WritesTheToolReference()
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", FrameworkDependent, ("Program.cs", Program), ("EchoTools.cs", Tools));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true");
        var index = File.ReadAllText(Path.Combine(folder, "docs", "reference", "tools", "index.md"));
        var family = File.ReadAllText(Path.Combine(folder, "docs", "reference", "tools", "echo.md"));

        Assert.True(exitCode == 0, output);
        Assert.Contains("3 tools in 1 family.", index, StringComparison.Ordinal);
        Assert.Contains("| `text` | `string` | yes | — | What to stamp. |", family, StringComparison.Ordinal);
        Assert.DoesNotContain("`clock`", family, StringComparison.Ordinal);
        Assert.DoesNotContain("`echoes`", family, StringComparison.Ordinal);
        Assert.Contains("| [`echo`](echo.md#echo) | Echo | Answer with the text it was given. |", index, StringComparison.Ordinal);
        Assert.Contains("*read-only · repeat-safe · stays on this machine.*", family, StringComparison.Ordinal);
        Assert.Contains("| `text` | `string` | yes | — | What to shout. |", family, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "docs", "reference", "schemas", "tools.json")));
    }

    /// <summary>SnailMcpInstall publishes into a folder of its own under the root and points current at it.</summary>
    [Fact]
    public void Publish_WithInstall_PointsCurrentAtTheNewPublication()
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", FrameworkDependent, ("Program.cs", Program), ("EchoTools.cs", Tools));
        var root = Path.Combine(folder, "installed");

        Git(folder, "init", "-q");
        Git(folder, "add", "-A");
        Git(folder, "-c", "user.name=test", "-c", "user.email=test@example.com", "commit", "-q", "-m", "sample");

        var (exitCode, output) = SdkFeed.Dotnet(folder, "publish", "-c", "Release", "-p:SnailMcpInstall=true", $"-p:SnailMcpRoot={root}");
        var current = Path.Combine(root, "Snail.MCP.Sample", "current");

        Assert.True(exitCode == 0, output);
        Assert.True(File.Exists(Path.Combine(current, "Snail.MCP.Sample.dll")), output);
    }

    /// <summary>With nothing loaded, the commands that load the job are printed; launchd is not touched.</summary>
    [Fact]
    public void LaunchJob_NotLoaded_PrintsTheCommandsThatLoadIt()
    {
        var (output, calls, _) = LaunchJob(loaded: []);

        Assert.Contains("s3l.mcp.sample is not loaded", output, StringComparison.Ordinal);
        Assert.Contains("launchctl bootstrap gui/", output, StringComparison.Ordinal);
        Assert.DoesNotContain("bootout", output, StringComparison.Ordinal);
        Assert.Empty(calls);
    }

    /// <summary>The same server's job under its snail name holds the same port: what retires it comes before what loads the new one.</summary>
    [Fact]
    public void LaunchJob_WithTheSnailNamedJobLoaded_RetiresItFirst()
    {
        var (output, calls, _) = LaunchJob(loaded: [("com.snail.mcp.sample", "/Agents/com.snail.mcp.sample.plist")]);

        var retired = output.IndexOf("/com.snail.mcp.sample", StringComparison.Ordinal);
        var loaded = output.IndexOf("launchctl bootstrap", StringComparison.Ordinal);

        Assert.Contains("launchctl bootout gui/", output, StringComparison.Ordinal);
        Assert.Contains("rm \"/Agents/com.snail.mcp.sample.plist\"", output, StringComparison.Ordinal);
        Assert.InRange(retired, 0, loaded);
        Assert.Empty(calls);
    }

    /// <summary>A job loaded from the very plist just written is restarted, and starts on the new publication.</summary>
    [Fact]
    public void LaunchJob_LoadedFromTheJobJustWritten_IsRestarted()
    {
        var (output, calls, _) = LaunchJob(loaded: [("s3l.mcp.sample", "{job}")]);

        Assert.Contains("s3l.mcp.sample restarted", output, StringComparison.Ordinal);
        Assert.Contains(calls, call => call.StartsWith("kickstart -k gui/", StringComparison.Ordinal) && call.EndsWith("/s3l.mcp.sample", StringComparison.Ordinal));
    }

    /// <summary>A job loaded from another plist would keep it through a restart: the commands that replace it are printed instead.</summary>
    [Fact]
    public void LaunchJob_LoadedFromAnotherPlist_PrintsHowToReplaceIt()
    {
        var (output, calls, job) = LaunchJob(loaded: [("s3l.mcp.sample", "{other}")]);

        Assert.Contains("not the job just written", output, StringComparison.Ordinal);
        Assert.Contains("launchctl bootout gui/", output, StringComparison.Ordinal);
        Assert.Contains($"cp \"{job}\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("restarted", output, StringComparison.Ordinal);
        Assert.Empty(calls);
    }

    /// <summary>
    /// A job loaded from a copy of the one written, with a token added by hand, would lose the token if the new job were
    /// loaded: the install names the variables and the settings file they belong in, and restarts nothing. Its plists
    /// are compared by plutil, which only macOS has.
    /// </summary>
    [Fact]
    public void LaunchJob_LoadedFromACopyWithVariablesAddedByHand_SaysToMoveThemToTheSettingsFile()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var (output, calls, _) = LaunchJob(loaded: [("s3l.mcp.sample", "{handwritten}")], template: RealTemplate);

        Assert.Contains("only in its EnvironmentVariables", output, StringComparison.Ordinal);
        Assert.Contains("~/.config/s3l-mcp-sample/config.json", output, StringComparison.Ordinal);
        Assert.Contains("SNAIL_MCP_SAMPLE_ACCESS_TOKEN -> \"AccessToken\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("figd_by-hand", output, StringComparison.Ordinal);
        Assert.DoesNotContain("SNAIL_MCP_SAMPLE_TRANSPORT", output, StringComparison.Ordinal);
        Assert.Empty(calls);
    }

    /// <summary>A template whose comment was reworded writes the job launchd already holds: it is restarted, not reloaded. Compared by plutil, which only macOS has.</summary>
    [Fact]
    public void LaunchJob_LoadedFromACopyWhoseCommentDiffers_IsRestarted()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var (output, calls, _) = LaunchJob(loaded: [("s3l.mcp.sample", "{reworded}")], template: RealTemplate);

        Assert.Contains("s3l.mcp.sample restarted", output, StringComparison.Ordinal);
        Assert.Contains(calls, call => call.StartsWith("kickstart -k gui/", StringComparison.Ordinal));
    }

    private const string RealTemplate = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <!-- The template's own words, which the loaded copy may have lost. -->
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>s3l.mcp.sample</string>
            <key>ProgramArguments</key>
            <array><string>{{CURRENT}}/Snail.MCP.Sample</string></array>
            <key>EnvironmentVariables</key>
            <dict><key>SNAIL_MCP_SAMPLE_TRANSPORT</key><string>Http</string></dict>
        </dict>
        </plist>

        """;

    /// <summary>
    /// The launchd step of an install, run alone against a temporary home and a stand-in for launchctl that answers
    /// print for the jobs given and records every other call: nothing is published, launchd is never asked.
    /// </summary>
    /// <remarks><c>{handwritten}</c> names a copy of the job the install writes from <paramref name="template"/>, with a token added by hand; <c>{reworded}</c> one whose comment says other words.</remarks>
    private (string Output, string[] Calls, string Job) LaunchJob((string Label, string Plist)[] loaded, string? template = null)
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", "<PropertyGroup><SnailMcpTransport>Http</SnailMcpTransport></PropertyGroup>");
        var home = Directory.CreateDirectory(Path.Combine(folder, "home")).FullName;
        var job = Path.Combine(home, "s3l.mcp.sample.plist");
        var other = Path.Combine(folder, "other.plist");
        var launchctl = Path.Combine(folder, "launchctl");

        Directory.CreateDirectory(Path.Combine(folder, "deploy"));
        var handwritten = Path.Combine(folder, "handwritten.plist");
        var reworded = Path.Combine(folder, "reworded.plist");

        template ??= "<plist><string>{{CURRENT}}/Snail.MCP.Sample</string></plist>\n";
        File.WriteAllText(Path.Combine(folder, "deploy", "s3l.mcp.sample.plist"), template);
        File.WriteAllText(other, "<plist><string>an older job</string></plist>\n");
        File.WriteAllText(handwritten, template
            .Replace("{{CURRENT}}", $"{home}/current", StringComparison.Ordinal)
            .Replace("<!-- The template's own words, which the loaded copy may have lost. -->\n", string.Empty, StringComparison.Ordinal)
            .Replace("<string>Http</string>", "<string>Http</string><key>SNAIL_MCP_SAMPLE_ACCESS_TOKEN</key><string>figd_by-hand</string>", StringComparison.Ordinal));
        File.WriteAllText(reworded, template
            .Replace("{{CURRENT}}", $"{home}/current", StringComparison.Ordinal)
            .Replace("The template's own words", "Words the template no longer has", StringComparison.Ordinal));
        File.WriteAllLines(Path.Combine(folder, "loaded.txt"), loaded.Select(entry => $"{entry.Label} {entry.Plist.Replace("{job}", job, StringComparison.Ordinal).Replace("{other}", other, StringComparison.Ordinal).Replace("{handwritten}", handwritten, StringComparison.Ordinal).Replace("{reworded}", reworded, StringComparison.Ordinal)}"));
        File.WriteAllText(launchctl, """
            #!/bin/sh
            here=$(dirname "$0")
            if [ "$1" = print ]; then
              line=$(grep "^${2##*/} " "$here/loaded.txt") || exit 113
              printf '%s = {\n\tpath = %s\n}\n' "${2##*/}" "${line#* }"
              exit 0
            fi
            echo "$*" >> "$here/calls.txt"
            """);
        File.SetUnixFileMode(launchctl, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var (exitCode, output) = SdkFeed.Dotnet(folder, "msbuild", "-t:_SnailMcpLaunchJob", "-nologo",
            "-p:_IsSnailMcpInstall=true", $"-p:_SnailMcpHome={home}", $"-p:_SnailLaunchctl={launchctl}");
        var calls = Path.Combine(folder, "calls.txt");

        Assert.True(exitCode == 0, output);
        Assert.Contains($"{home}/current/Snail.MCP.Sample", File.ReadAllText(job), StringComparison.Ordinal);

        return (output, File.Exists(calls) ? File.ReadAllLines(calls) : [], job);
    }

    private static void Git(string folder, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = folder };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var git = System.Diagnostics.Process.Start(start)!;

        git.WaitForExit();
    }
}

using Snail.Sdk.Docs;

namespace Snail.Sdk.Sdk.Tests;

/// <summary>docs/tools.json is checked before a page is written, and the generator stops on one line, never a stack trace.</summary>
[Collection(SdkFeed.Collection)]
public sealed class DocsToolTests(SdkFeed sdk) : IDisposable
{
    private const string Server = """
        <PropertyGroup>
          <SnailMcpNative>false</SnailMcpNative>
          <RootNamespace>Server</RootNamespace>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="ModelContextProtocol" Version="2.3.1" />
        </ItemGroup>
        """;

    private const string Program = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Server;
        using Snail.Sdk.Mcp.Hosting;

        var builder = McpHost.CreateBuilder(args);

        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<EchoTools>();

        await builder.Build().RunAsync();
        """;

    private const string SdkToolsServer = """
        <PropertyGroup>
          <SnailMcpNative>false</SnailMcpNative>
          <RootNamespace>Server</RootNamespace>
        </PropertyGroup>
        <ItemGroup>
          <PackageReference Include="ModelContextProtocol" Version="2.3.1" />
          <PackageReference Include="Snail.Sdk.Mcp.Programs" />
        </ItemGroup>
        """;

    private const string SdkToolsProgram = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Server;
        using Snail.Sdk.Mcp.Hosting;
        using Snail.Sdk.Mcp.Hosting.Discovery;
        using Snail.Sdk.Mcp.Programs;

        var builder = McpHost.CreateBuilder(args);

        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<EchoTools>()
            .WithTools<LoudTools>()
            .WithToolDiscovery()
            .WithToolSequences()
            .WithSkills()
            .WithPrograms<ToolCommands>(program => program.MaxCalls = 50);

        await builder.Build().RunAsync();
        """;

    private const string LoudTools = """
        using System.ComponentModel;
        using ModelContextProtocol.Server;
        using Snail.Sdk.Mcp.Hosting.Discovery;

        namespace Server;

        /// <summary>Says things loudly, once the skill is loaded.</summary>
        [McpServerToolType, Skill("loud", "Saying things loudly.")]
        public sealed class LoudTools
        {
            /// <summary>Answers with the text in capitals.</summary>
            [McpServerTool(Name = "shout", ReadOnly = true), Description("Answer with the text in capitals.")]
            public static string Shout([Description("What to shout.")] string text) => text.ToUpperInvariant();
        }
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
            [McpServerTool(Name = "echo", ReadOnly = true), Description("Answer with the text it was given.")]
            public static string Echo([Description("What to answer with.")] string text) => text;
        }
        """;

    private readonly string _root = TestRoot.Create();

    [Fact]
    public void Settings_ThatAreValid_AreRead()
    {
        var settings = CatalogSettings.Read(Write("""
            // Families in reading order.
            { "intro": [ "Tools." ], "families": [ { "type": "EchoTools", "slug": "echo-2", "title": "Echo" } ] }
            """));

        Assert.Equal(new FamilyName("EchoTools", "echo-2", "Echo"), Assert.Single(settings.Families));
    }

    [Fact]
    public void Settings_NamingToolsByName_AreRead()
    {
        var settings = CatalogSettings.Read(Write("""
            { "families": [ { "type": "EchoTools", "slug": "echo", "title": "Echo" }, { "tools": [ "find_tool", "run_tools" ], "slug": "discovery", "title": "Finding a tool" } ] }
            """));

        Assert.Equal(["find_tool", "run_tools"], settings.Families[1].Tools);
        Assert.Null(settings.Families[1].Type);
        Assert.Equal(new HashSet<string>(["find_tool", "run_tools"]), settings.NamedTools());
    }

    [Fact]
    public void Settings_Missing_AreTheDefaults() => Assert.Empty(CatalogSettings.Read(Path.Combine(_root, "absent.json")).Families);

    [Theory]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "../echo", "title": "Echo" } ] }""", "families[0]: the slug \"../echo\" is not lower-case letters, digits and hyphens")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "", "title": "Echo" } ] }""", "families[0]: the slug \"\" is not lower-case letters, digits and hyphens")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "Echo", "title": "Echo" } ] }""", "families[0]: the slug \"Echo\" is not lower-case letters, digits and hyphens")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "index", "title": "Echo" } ] }""", "families[0]: the slug index is the page of every tool")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "echo", "title": " " } ] }""", "families[0] has no title")]
    [InlineData("""{ "families": [ { "slug": "echo", "title": "Echo" } ] }""", "families[0] names no class (type)")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "tools": [ "find_tool" ], "slug": "echo", "title": "Echo" } ] }""", "families[0] names a class (type) and tools: a family is one or the other")]
    [InlineData("""{ "families": [ { "tools": [], "slug": "discovery", "title": "Discovery" } ] }""", "families[0]: tools must be a list of tool names")]
    [InlineData("""{ "families": [ { "tools": [ " " ], "slug": "discovery", "title": "Discovery" } ] }""", "families[0]: tools must be a list of tool names")]
    [InlineData("""{ "families": [ { "tools": [ "find_tool" ], "slug": "a", "title": "A" }, { "tools": [ "find_tool" ], "slug": "b", "title": "B" } ] }""", "the tool find_tool is named by more than one family")]
    [InlineData("""{ "families": [ null ] }""", "families[0] is empty")]
    [InlineData("""{ "families": [ { "type": "A", "slug": "same", "title": "A" }, { "type": "B", "slug": "same", "title": "B" } ] }""", "the slug same is given to more than one family")]
    [InlineData("""{ "families": [ { "type": "A", "slug": "a", "title": "A" }, { "type": "A", "slug": "b", "title": "B" } ] }""", "the class A is named by more than one family")]
    [InlineData("""{ "families": null }""", "families must be a list")]
    [InlineData("""{ "familes": [] }""", "'familes' could not be mapped")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "echo", "title": "Echo", "icon": "x" } ] }""", "'icon' could not be mapped")]
    [InlineData("""{ "families": [ """, "tools.json: ")]
    public void Settings_ThatCannotMakePages_AreRefused(string json, string seen)
    {
        var path = Write(json);

        var error = Assert.Throws<DocsError>(() => CatalogSettings.Read(path));

        Assert.Equal(DocsError.InvalidSettings, error.Code);
        Assert.StartsWith($"{path}: ", error.Message, StringComparison.Ordinal);
        Assert.Contains(seen, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Error_IsOneMSBuildLine() =>
        Assert.Equal("error S3LD0001: docs/tools.json: families[0] has no title", DocsError.Line(DocsError.InvalidSettings, "docs/tools.json: families[0] has no title"));

    /// <summary>A family naming a class the server lacks stops the build with the id and the class, not with a stack trace.</summary>
    [Fact]
    public void Build_WithAFamilyOfAClassTheServerLacks_FailsOnOneLine()
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", Server, ("Program.cs", Program), ("EchoTools.cs", Tools),
            ("tools.json", """{ "families": [ { "type": "EchoTools", "slug": "echo", "title": "Echo" }, { "type": "GoneTools", "slug": "gone", "title": "Gone" } ] }"""));

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true", $"-p:SnailReferenceSettings={Path.Combine(folder, "tools.json")}");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("error S3LD0002: docs/tools.json names families of classes the server does not declare tools in: GoneTools", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", output, StringComparison.Ordinal);
        Assert.DoesNotContain("   at Snail.Sdk.Docs.", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The tools Snail.Sdk registers — find_tool, run_tools, run_program, the skill tools — are in the reference as a client
    /// receives them: named by the server's short name, described from its options; a family names them by name, the ones
    /// no family names get a page of their own, and a family naming a tool the server lacks stops the build.
    /// </summary>
    /// <remarks>Snail.MCP.Sandbox lost its page of find_tool in its move to 0.5.0 (1c4f3ab): the generator read classes only.</remarks>
    [Fact]
    public void Build_OfAServerWithTheSdksTools_DocumentsThem()
    {
        var folder = sdk.Project("Snail.Sdk.Mcp", "Snail.MCP.Sample", SdkToolsServer, ("Program.cs", SdkToolsProgram), ("EchoTools.cs", Tools), ("LoudTools.cs", LoudTools),
            ("tools.json", """
                { "families": [
                  { "type": "EchoTools", "slug": "echo", "title": "Echo" },
                  { "type": "LoudTools", "slug": "loud", "title": "Loud" },
                  { "tools": [ "find_tool", "run_tools", "run_program" ], "slug": "discovery", "title": "Finding a tool" },
                  { "tools": [ "sample_skills", "sample_enable_skill", "sample_disable_skill" ], "slug": "skills", "title": "Skills" }
                ] }
                """),
            ("gone.json", """{ "families": [ { "type": "EchoTools", "slug": "echo", "title": "Echo" }, { "type": "LoudTools", "slug": "loud", "title": "Loud" }, { "tools": [ "find_tool", "run_programs" ], "slug": "discovery", "title": "Finding a tool" } ] }"""));
        var reference = Path.Combine(folder, "docs", "reference");

        var (named, namedOutput) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true", $"-p:SnailReferenceSettings={Path.Combine(folder, "tools.json")}");
        var discovery = File.ReadAllText(Path.Combine(reference, "tools", "discovery.md"));
        var skills = File.ReadAllText(Path.Combine(reference, "tools", "skills.md"));
        var index = File.ReadAllText(Path.Combine(reference, "tools", "index.md"));
        var schemas = File.ReadAllText(Path.Combine(reference, "schemas", "tools.json"));

        Assert.True(named == 0, namedOutput);
        Assert.DoesNotContain("S3LD0004", namedOutput, StringComparison.Ordinal);
        Assert.Contains("8 tools in 4 families", index, StringComparison.Ordinal);
        Assert.Contains("| [`find_tool`](discovery.md#find_tool) | Finding a tool | — |", index, StringComparison.Ordinal);
        Assert.Contains("| [`run_tools`](discovery.md#run_tools) | Finding a tool | — |", index, StringComparison.Ordinal);
        Assert.Contains("| [`run_program`](discovery.md#run_program) | Finding a tool | — |", index, StringComparison.Ordinal);
        Assert.Contains("| [`sample_enable_skill`](skills.md#sample_enable_skill) | Skills | — |", index, StringComparison.Ordinal);
        Assert.Contains("| [`shout`](loud.md#shout) | Loud | loud |", index, StringComparison.Ordinal);
        Assert.Contains("| `enable` |", discovery, StringComparison.Ordinal);
        Assert.Contains("| `steps` |", discovery, StringComparison.Ordinal);
        Assert.Contains("| `code` |", discovery, StringComparison.Ordinal);
        Assert.Contains("Limits: 50 commands", discovery, StringComparison.Ordinal);
        Assert.Contains("| `name` |", skills, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"run_program\"", schemas, StringComparison.Ordinal);
        Assert.Contains("\"name\": \"sample_skills\"", schemas, StringComparison.Ordinal);
        Assert.Contains("\"skill\": \"loud\"", schemas, StringComparison.Ordinal);

        var (unnamed, unnamedOutput) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true", $"-p:SnailReferenceSettings={Path.Combine(folder, "absent.json")}");
        var rest = File.ReadAllText(Path.Combine(reference, "tools", "discovery.md"));
        var restIndex = File.ReadAllText(Path.Combine(reference, "tools", "index.md"));

        Assert.True(unnamed == 0, unnamedOutput);
        Assert.StartsWith("# Finding and combining tools", rest, StringComparison.Ordinal);
        Assert.Contains("| [`sample_disable_skill`](discovery.md#sample_disable_skill) | Finding and combining tools | — |", restIndex, StringComparison.Ordinal);
        Assert.Contains("| [`run_program`](discovery.md#run_program) | Finding and combining tools | — |", restIndex, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(reference, "tools", "skills.md")));

        var (gone, goneOutput) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true", $"-p:SnailReferenceSettings={Path.Combine(folder, "gone.json")}");

        Assert.NotEqual(0, gone);
        Assert.Contains("error S3LD0002: docs/tools.json names tools the server does not register: run_programs", goneOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// A server's registration that creates its data folder at composition, as Snail.MCP.Memory's AddSqliteStorage does,
    /// creates it in a home of the generator's, deleted afterwards — never in the developer's — and the server reads no
    /// settings file of the developer's: not the broken one in the working directory the build runs in.
    /// </summary>
    /// <remarks>Snail.MCP.Plans and Snail.MCP.Memory created ~/.s3l-mcp-plans and ~/.s3l-mcp-memory on a docs build of 0.5.0-local.3.</remarks>
    [Fact]
    public void Build_OfAServerWhoseRegistrationWritesItsData_LeavesTheDevelopersHomeAlone()
    {
        var name = $"docshome{Guid.NewGuid():N}"[..20];
        var realData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), $".s3l-mcp-{name}");
        var seen = Path.Combine(_root, $"{name}.seen");
        var registration = $$"""
            using Microsoft.Extensions.DependencyInjection;
            using Snail.Sdk.Mcp.Hosting;

            namespace Server;

            /// <summary>The notes' storage.</summary>
            public static class NotesRegistration
            {
                /// <summary>Opens the notes' storage, creating its folder, as a server opening its database at registration does.</summary>
                public static IServiceCollection AddNotes(this IServiceCollection services)
                {
                    var data = ServerFolders.DataDirectory("{{name}}");

                    Directory.CreateDirectory(data);
                    File.WriteAllText(Path.Combine(data, "notes.db"), "opened");
                    File.WriteAllLines(@"{{seen}}", [ServerFolders.Home, Environment.GetEnvironmentVariable("SNAIL_MCP_{{name.ToUpperInvariant()}}_CONFIG") ?? string.Empty, Directory.GetCurrentDirectory()]);

                    return services;
                }
            }
            """;
        var program = """
            using Microsoft.Extensions.DependencyInjection;
            using Microsoft.Extensions.Hosting;
            using Server;
            using Snail.Sdk.Mcp.Hosting;
            using Snail.Sdk.Mcp.Hosting.Discovery;

            var builder = McpHost.CreateBuilder(args);

            builder.Services.AddNotes();
            builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<EchoTools>().WithToolDiscovery();

            await builder.Build().RunAsync();
            """;
        var folder = sdk.Project("Snail.Sdk.Mcp", $"Snail.MCP.{name}", Server, ("Program.cs", program), ("EchoTools.cs", Tools), ("NotesRegistration.cs", registration),
            ($".s3l-mcp-{name}.json", "{ this is no JSON"));

        Assert.False(Directory.Exists(realData), $"{realData} is there before the build");

        var (exitCode, output) = SdkFeed.Dotnet(folder, "build", "-p:SnailReference=true", $"-p:SnailReferenceSettings={Path.Combine(folder, "absent.json")}");
        var sawHome = File.Exists(seen) ? File.ReadAllLines(seen) : [];
        var leaked = Directory.Exists(realData);

        if (leaked)
            Directory.Delete(realData, recursive: true);

        Assert.True(exitCode == 0, output);
        Assert.False(leaked, $"the build created {realData} in the developer's home");
        Assert.DoesNotContain("S3LD0004", output, StringComparison.Ordinal);
        Assert.Contains("find_tool", File.ReadAllText(Path.Combine(folder, "docs", "reference", "tools", "index.md")), StringComparison.Ordinal);
        Assert.Equal(3, sawHome.Length);
        Assert.NotEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), sawHome[0]);
        Assert.False(Directory.Exists(sawHome[0]), $"the server's home {sawHome[0]} was left behind");
        Assert.NotEqual(string.Empty, sawHome[1]);
        Assert.False(File.Exists(sawHome[1]));
        Assert.NotEqual(Path.GetFullPath(folder), Path.GetFullPath(sawHome[2]));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}", "tools.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        return path;
    }
}

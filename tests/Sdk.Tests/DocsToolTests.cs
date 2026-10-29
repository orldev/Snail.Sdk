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
          <PackageReference Include="ModelContextProtocol" Version="2.3.0" />
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
    public void Settings_Missing_AreTheDefaults() => Assert.Empty(CatalogSettings.Read(Path.Combine(_root, "absent.json")).Families);

    [Theory]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "../echo", "title": "Echo" } ] }""", "families[0]: the slug \"../echo\" is not lower-case letters, digits and hyphens")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "", "title": "Echo" } ] }""", "families[0]: the slug \"\" is not lower-case letters, digits and hyphens")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "Echo", "title": "Echo" } ] }""", "families[0]: the slug \"Echo\" is not lower-case letters, digits and hyphens")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "index", "title": "Echo" } ] }""", "families[0]: the slug index is the page of every tool")]
    [InlineData("""{ "families": [ { "type": "EchoTools", "slug": "echo", "title": " " } ] }""", "families[0] has no title")]
    [InlineData("""{ "families": [ { "slug": "echo", "title": "Echo" } ] }""", "families[0] names no class (type)")]
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

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}", "tools.json");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);

        return path;
    }
}

using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>One chain of settings files for every server, and its variables over the file.</summary>
public sealed class ServerSettingsTests : IDisposable
{
    private readonly string _home = TestFolder.Create();
    private readonly string _work = TestFolder.Create();

    /// <summary>A file the variable names is the only one: a missing one reads nothing rather than a file the person did not mean.</summary>
    [Fact]
    public void Candidates_ANamedFile_IsTheOnlyOne()
    {
        var candidates = ServerSettings.Candidates("demo", _work, _home, "~/elsewhere.json");

        Assert.Equal([Path.Combine(_home, "elsewhere.json")], candidates);
    }

    /// <summary>Without the variable, the s3l names only: the names from before the rename are not read.</summary>
    [Fact]
    public void Candidates_WithoutTheVariable_AreTheS3lNamesOnly()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".config", "snail-mcp-demo"));

        var candidates = ServerSettings.Candidates("demo", _work, _home, " ");

        Assert.Equal(
            [
                Path.Combine(_work, ".s3l-mcp-demo.json"),
                Path.Combine(_work, "s3l-mcp-demo.json"),
                Path.Combine(_home, ".config", "s3l-mcp-demo", "config.json"),
                Path.Combine(_home, ".s3l-mcp-demo.json"),
            ],
            candidates);
    }

    /// <summary>The token kept in the settings file reaches the server; one in its variables wins, and a blank variable leaves the file's value.</summary>
    [Fact]
    public void TheFileIsRead_AndTheServersVariablesWin()
    {
        var file = Path.Combine(_home, "config.json");
        File.WriteAllText(file, """{ "AccessToken": "from-file", "Section": { "Field": "file" }, "Mode": "file" }""");
        IDictionary variables = new Hashtable
        {
            ["SNAIL_MCP_DEMO_SECTION__FIELD"] = "variable",
            ["SNAIL_MCP_DEMO_MODE"] = " ",
            ["SNAIL_MCP_OTHER_ACCESS_TOKEN"] = "another server's",
        };

        var settings = new ConfigurationBuilder().AddServerSettings("demo", file, variables).Build();

        Assert.Equal("from-file", settings["AccessToken"]);
        Assert.Equal("variable", settings["Section:Field"]);
        Assert.Equal("file", settings["Mode"]);
    }

    [Fact]
    public void WithoutAFile_OnlyTheVariablesAreRead()
    {
        IDictionary variables = new Hashtable { ["SNAIL_MCP_DEMO_ACCESS_TOKEN"] = "from-variable" };

        var settings = new ConfigurationBuilder().AddServerSettings("demo", null, variables).Build();

        Assert.Equal("from-variable", settings["AccessToken"]);
    }

    [Fact]
    public void ThePrefixAndTheFileVariable_FollowTheShortName()
    {
        Assert.Equal("SNAIL_MCP_CODECONTEXT_", ServerSettings.PrefixOf("codecontext"));
        Assert.Equal("SNAIL_MCP_FIGMA_CONFIG", ServerSettings.FileVariableOf("figma"));
    }

    /// <summary>The host builders read a server's settings by the command Snail.Sdk.Mcp writes into its assembly; any other program has none.</summary>
    [Fact]
    public void TheShortName_IsReadFromTheCommandWrittenIntoTheAssembly()
    {
        var server = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Demo"),
            AssemblyBuilderAccess.Run,
            [new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, ["Snail.Sdk.Mcp.ServerName", "s3l-mcp-demo"])]);

        Assert.Equal("demo", ServerSettings.NameOf(server));
        Assert.Null(ServerSettings.NameOf(typeof(ServerSettingsTests).Assembly));
        Assert.Null(ServerSettings.NameOf(null));
    }

    public void Dispose()
    {
        Directory.Delete(_home, recursive: true);
        Directory.Delete(_work, recursive: true);
    }
}

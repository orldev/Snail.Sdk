using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.Template.Tests;

/// <summary>The server as a program, started the way a client starts it: the build beside the tests, or the one SNAIL_TMPLSHORT_SERVER names.</summary>
public sealed class StdioContractTests
{
    private const string Assembly = "Snail.MCP.Template";

    private const string Variable = "SNAIL_TMPLSHORT_SERVER";

    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    /// <summary>A client knows the server by the command a person types to start it.</summary>
    [Fact]
    public async Task Initialize_AsAProgram_NamesTheServerByItsCommand()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        Assert.Equal("s3l-mcp-tmplshort", client.ServerInfo?.Name);
    }

    [Fact]
    public async Task Diagnose_AsAProgram_NamesTheServer()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var answer = await client.CallToolAsync("tmplshort_diagnose", new Dictionary<string, object?>());

        Assert.Contains(Assembly, answer.Text(), StringComparison.Ordinal);
    }
}

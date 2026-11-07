using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.Template.Tests;

/// <summary>The server over HTTP, started as a program on a free loopback port: the endpoint, its tools, the refusal of browsers.</summary>
public sealed class HttpContractTests : IAsyncLifetime
{
    private const string Assembly = "Snail.MCP.Template";

    private const string Variable = "SNAIL_TMPLSHORT_SERVER";

    private HttpServerProgram _server = null!;

    /// <summary>The server reads its address from SNAIL_MCP_TMPLSHORT_URL and maps no readiness path: it is ready when its endpoint answers.</summary>
    public async Task InitializeAsync() =>
        _server = await HttpServerProgram.StartAsync(Assembly, Variable, new Dictionary<string, string?> { ["SNAIL_MCP_TMPLSHORT_TRANSPORT"] = "http" }, readiness: null);

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Tools_OverHttp_KeepTheGuardRules()
    {
        await using var client = await _server.SessionAsync();

        var listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    /// <summary>A page in a browser always sends Origin and an agent's client never does: no web page may reach the server.</summary>
    [Fact]
    public async Task Endpoint_AsServed_RefusesBrowsersOnly() =>
        Assert.Empty(await HttpGuards.ProblemsAsync(_server.Endpoint));
}

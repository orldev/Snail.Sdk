#if (jobs)
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.JobClient;
#endif
using Snail.Sdk.Mcp.Testing;

namespace Snail.MCP.Template.Tests;

/// <summary>The server over HTTP, started as a program on a free loopback port: the endpoint, its tools, the grant of a session, the refusal of browsers.</summary>
public sealed class HttpContractTests : IAsyncLifetime
{
    private const string Assembly = "Snail.MCP.Template";

    private const string Variable = "SNAIL_TMPLSHORT_SERVER";

    private HttpServerProgram _server = null!;

    /// <summary>The server reads its address from SNAIL_MCP_TMPLSHORT_URL, is told HTTP by SNAIL_MCP_TMPLSHORT_TRANSPORT and is ready when /healthz answers.</summary>
    public async Task InitializeAsync() =>
        _server = await HttpServerProgram.StartAsync(Assembly, Variable);

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    public async Task Tools_OverHttp_KeepTheGuardRules()
    {
        await using var client = await _server.SessionAsync();

        var listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    [Fact]
    public async Task Diagnose_OverHttp_NamesTheServer()
    {
        await using var client = await _server.SessionAsync();

        var answer = await client.CallToolAsync("tmplshort_diagnose", new Dictionary<string, object?>());

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal(Assembly, answer.Data()!["server"]!.GetValue<string>());
    }

    /// <summary>A role given some of the tools sees those alone: the platform names them in the address, ?tool=.</summary>
    [Fact]
    public async Task Tools_GrantedOne_ListThatOneAlone()
    {
        await using var client = await _server.SessionAsync("tool=tmplshort_diagnose");

        var listed = await client.ListToolsAsync();

        Assert.Equal(["tmplshort_diagnose"], listed.Select(tool => tool.Name));
    }

    /// <summary>A tool the address did not grant is not listed, and a call to it is refused in the envelope, flagged an error.</summary>
    [Fact]
    public async Task Diagnose_NotGranted_IsHiddenAndRefused()
    {
        await using var client = await _server.SessionAsync("tool=tmplshort_other");

        var listed = await client.ListToolsAsync();
        var answer = await client.CallToolAsync("tmplshort_diagnose", new Dictionary<string, object?>());

        Assert.Empty(listed);
        Assert.True(answer.IsError);
        Assert.False(answer.IsOk());
        Assert.Contains("'tmplshort_diagnose' was not granted", answer.Error(), StringComparison.Ordinal);
        Assert.NotNull(answer.Hint());
    }

#if (jobs)
    /// <summary>A session over HTTP awaits a job by being told, as one over stdio does: the resource's subscription is the session's.</summary>
    [Fact]
    public async Task Work_OverHttp_IsAwaitedWithoutPolling()
    {
        await using var client = await _server.SessionAsync();

        var report = await client.CallJobAsync("tmplshort_work", new Dictionary<string, object?> { ["upTo"] = 3 });

        Assert.Equal(JobState.Succeeded, report.State);
        Assert.Equal(3, report.Result!.Value.GetProperty("count").GetInt32());
    }

#endif
    /// <summary>A page in a browser always sends Origin and an agent's client never does: no web page may reach the server.</summary>
    [Fact]
    public async Task Endpoint_AsServed_RefusesBrowsersOnly() =>
        Assert.Empty(await HttpGuards.ProblemsAsync(_server.Endpoint));
}

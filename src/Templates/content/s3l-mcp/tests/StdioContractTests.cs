#if (jobs)
using Snail.MCP.Template.Tools;
using Snail.Sdk.Mcp.Hosting.Jobs;
using Snail.Sdk.Mcp.JobClient;
#endif
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

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal(Assembly, answer.Data()!["server"]!.GetValue<string>());
    }
#if (jobs)

    /// <summary>A job is awaited by being told: called with no wait, subscribed to, answered with its result when it ends.</summary>
    [Fact]
    public async Task Work_AsAJob_IsAwaitedWithoutPolling()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var report = await client.CallJobAsync("tmplshort_work", new Dictionary<string, object?> { ["upTo"] = 3 });

        Assert.Equal(JobState.Succeeded, report.State);
        Assert.Equal(3, report.Result!.Value.GetProperty("count").GetInt32());
    }

    /// <summary>A client that cannot wait is answered with the running job.</summary>
    [Fact]
    public async Task Work_AskedWithNoWait_AnswersWithTheRunningJob()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var started = await client.CallToolAsync("tmplshort_work", new Dictionary<string, object?> { ["upTo"] = 5, ["wait"] = 0 });

        Assert.StartsWith("job://tmplshort/work/", started.Report()!.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Work_AskedWithNoWait_IsReadAsAResource()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);
        var started = await client.CallToolAsync("tmplshort_work", new Dictionary<string, object?> { ["upTo"] = 5, ["wait"] = 0 });

        var read = await client.ReadJobAsync(started.Report()!.Uri);

        Assert.Equal(started.Report()!.Uri, read.Uri);
    }

    /// <summary>A number out of range was counted to the nearest bound, silently: it is refused for what the call sent.</summary>
    [Fact]
    public async Task Work_PastTheMostSteps_IsRefusedForItsArguments()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var refused = await client.CallToolAsync("tmplshort_work", new Dictionary<string, object?> { ["upTo"] = WorkTools.MostSteps + 1 });

        Assert.Equal("arguments", refused.Type());
    }

    /// <summary>A limit written in a description drifts from the one in code; the words are held to the constant.</summary>
    [Fact]
    public async Task Work_AsListed_NamesTheMostSteps()
    {
        await using var client = await ServerProgram.StartAsync(Assembly, Variable);

        var listed = await client.ListToolsAsync();
        var upTo = listed.Single(tool => string.Equals(tool.Name, "tmplshort_work", StringComparison.Ordinal)).ProtocolTool.InputSchema.GetProperty("properties").GetProperty("upTo");

        Assert.Contains($"1 to {WorkTools.MostSteps}", upTo.GetProperty("description").GetString(), StringComparison.Ordinal);
    }
#endif
}

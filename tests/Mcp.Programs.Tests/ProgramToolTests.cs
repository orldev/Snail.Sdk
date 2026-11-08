using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Programs.Tests;

/// <summary>run_program through a real server pipeline: the tool as a client lists it, the gate before every command, and the answer's envelope.</summary>
public sealed partial class ProgramToolTests
{
    private static readonly FixedAddress Everything = new();

    [Fact]
    public async Task Tool_IsListed_AsAForwarder_DescribedWithItsLimits()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options =>
        {
            options.Global = "notes";
            options.MaxCalls = 50;
        }));

        var tools = await stand.Client.ListToolsAsync();
        var program = tools.Single(tool => tool.Name == "run_program");
        var registered = stand.Host.Services.GetServices<ModelContextProtocol.Server.McpServerTool>().Single(tool => tool.ProtocolTool.Name == "run_program");

        Assert.Contains("notes.<command>(", program.Description, StringComparison.Ordinal);
        Assert.Contains("Limits: 50 commands, 1000000 statements, calls nested 256 deep, 32 MB of memory, and 120 seconds unless timeoutSeconds gives up to 600.", program.Description, StringComparison.Ordinal);
        Assert.True(program.ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(ToolForwarding.IsForwarder(registered));
        Assert.Empty(ToolGuards.Problems([program.ProtocolTool]));
    }

    [Fact]
    public async Task Program_OverTheServersCatalog_AnswersWithResultLogAndCalls()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("notes.write({ text: 'a' }); log('wrote'); return notes.read().count;");

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal("""{"result":1,"log":["wrote"],"calls":[{"command":"write","ok":true,"milliseconds":0},{"command":"read","ok":true,"milliseconds":0}]}""", Timeless(answer.Data()!.ToJsonString()));
    }

    /// <summary>A role granted notes_read and run_program wrote through run_program until the catalog asked the grant (Snail.MCP.Git be181ac).</summary>
    [Fact]
    public async Task WithheldCommand_IsRefused_AndTheProgramCatchesIt()
    {
        var granted = new FixedAddress(("tool", "notes_read"), ("tool", "run_program"));
        await using var stand = await ProgramStand.StartAsync(granted, server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("try { notes.write({ text: 'x' }); } catch (e) { return e.type + ': ' + e.error; }");

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal("refused: 'notes_write' was not granted to this session, which may use notes_read, run_program", answer.Data()!["result"]!.GetValue<string>());
        Assert.Empty(stand.Notes.Written);
    }

    [Fact]
    public async Task StoppedProgram_AnswersTheFailure_WithItsTypeAndWhatItSent()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("notes.write({ text: 'kept' });\nnotes.wirte({ text: 'lost' });");

        Assert.True(answer.IsError);
        Assert.False(answer.IsOk());
        Assert.Equal("no command 'wirte'; the nearest: write", answer.Error());
        Assert.Contains("\"type\":\"noSuchCommand\"", answer.Text(), StringComparison.Ordinal);
        Assert.Equal("write", answer.Data()!["calls"]![0]!["command"]!.GetValue<string>());
        Assert.Equal(["kept"], stand.Notes.Written);
    }

    [Fact]
    public async Task Result_PastTheBudget_IsLeftOut_WithANote()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options =>
        {
            options.Global = "notes";
            options.ResultBytes = 1024;
        }));

        var answer = await stand.RunAsync("return 'x'.repeat(5000);");
        var data = answer.Data()!;

        Assert.True(answer.IsOk());
        Assert.Null(data["result"]);
        Assert.True(data["isOmitted"]!.GetValue<bool>());
        Assert.Equal("the result was 5002 bytes, more than the 1024 an answer may carry; return what matters", data["note"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public async Task Timeout_OutOfRange_IsRefused_BeforeAnythingRuns(int seconds)
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("notes.write({ text: 'x' });", seconds);

        Assert.Equal($"timeoutSeconds is {seconds}, and a program is given 1 to 600 seconds", answer.Error());
        Assert.Empty(stand.Notes.Written);
    }

    [Fact]
    public async Task EmptyProgram_IsRefused_WithTheCommandsNamed()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("  ");

        Assert.Equal("a program needs code", answer.Error());
        Assert.Equal("the program calls notes.<command>({ … }); the commands are read, write", answer.Hint());
    }

    [Theory]
    [InlineData("log")]
    [InlineData("input")]
    [InlineData("not-a-name")]
    public void Registration_WithAGlobalThatCannotBeOne_FailsAtStartup(string global)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddMcpServer().WithPrograms<NoteCommands>(options => options.Global = global));
    }

    [Fact]
    public async Task ToolCommands_CallTheServersTools_AsTheProtocolWould()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<ToolCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("""
            notes.write({ text: 'one' });
            notes.write({ text: 'two' });
            return { names: Object.keys(notes), notes: notes.read().notes, found: notes.find_tool({ task: 'reading' }) };
            """);

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal("""{"names":["find_tool","read","write"],"notes":["one","two"],"found":"notes_read does reading"}""", answer.Data()!["result"]!.ToJsonString());
        Assert.Equal(["one", "two"], stand.Notes.Written);
    }

    [Fact]
    public async Task ToolCommands_ATool_ThatFails_ThrowsItsEnvelopeIntoTheProgram()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<ToolCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("try { notes.write({ text: '' }); } catch (e) { return [e.type, e.error, e.hint]; }");

        Assert.Equal("""["empty","a note needs text","write something"]""", answer.Data()!["result"]!.ToJsonString());
    }

    [Fact]
    public async Task ToolCommands_AWithheldTool_IsRefused_AndCatchable()
    {
        var granted = new FixedAddress(("tool", "notes_read"), ("tool", "run_program"));
        await using var stand = await ProgramStand.StartAsync(granted, server => server.WithPrograms<ToolCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("try { notes.write({ text: 'x' }); return 'wrote'; } catch (e) { return e.type; }");

        Assert.Equal("refused", answer.Data()!["result"]!.GetValue<string>());
        Assert.Empty(stand.Notes.Written);
    }

    /// <summary>A catalog that needs the session, the services of the request or its progress token reads them from the request it is handed.</summary>
    [Fact]
    public async Task Command_IsHandedTheRequestOfRunProgram()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server =>
        {
            server.Services.AddSingleton<RequestCommands>();

            return server.WithPrograms<RequestCommands>(options => options.Global = "notes");
        });

        var answer = await stand.RunAsync("return notes.request();");

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal("""{"tool":"run_program","code":"return notes.request();","notes":0}""", answer.Data()!["result"]!.ToJsonString());
    }

    /// <summary>A catalog written before the request was handed over implements only the call without it, and runs unchanged.</summary>
    [Fact]
    public async Task Runner_GivenNoRequest_CallsTheCommandWithoutIt()
    {
        var run = await new ProgramRunner(new ProgramOptions { Global = "notes" }).RunAsync(new RequestCommands(), "try { notes.request(); } catch (e) { return e.type; }");

        Assert.Equal("noRequest", run.Result!.GetValue<string>());
    }

    [Fact]
    public async Task ToolCommands_NeverOfferAForwarder()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<ToolCommands>(options => options.Global = "notes"));

        var answer = await stand.RunAsync("notes.run_tools();");

        Assert.Equal("noSuchCommand", Type(answer.Text()));
    }

    private static string Timeless(string json) => Milliseconds().Replace(json, "\"milliseconds\":0");

    [GeneratedRegex("\"milliseconds\":\\d+")]
    private static partial Regex Milliseconds();

    private static string? Type(string envelope) => System.Text.Json.Nodes.JsonNode.Parse(envelope)?["type"]?.GetValue<string>();
}

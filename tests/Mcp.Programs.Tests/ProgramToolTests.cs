using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Hosting.Answers;
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
        Assert.Equal("notGranted: 'notes_write' was not granted to this session, which may use notes_read, run_program", answer.Data()!["result"]!.GetValue<string>());
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

    /// <summary>A server that gives its programs longer than the default ceiling means a call may give them as long.</summary>
    [Fact]
    public async Task Registration_WithATimeoutPastTheCeiling_RaisesTheCeiling()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options =>
        {
            options.Global = "notes";
            options.Timeout = TimeSpan.FromMinutes(15);
        }));

        var options = stand.Host.Services.GetRequiredService<ProgramOptions>();
        var tools = await stand.Client.ListToolsAsync();

        Assert.Equal(TimeSpan.FromMinutes(15), options.MaxTimeout);
        Assert.Contains("900 seconds unless timeoutSeconds gives up to 900.", tools.Single(tool => tool.Name == "run_program").Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(-5, 600)]
    [InlineData(120, 0)]
    [InlineData(120, -1)]
    public void Registration_WithATimeoutOrCeilingThatCannotBeOne_FailsAtStartup(int timeout, int ceiling)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddMcpServer().WithPrograms<NoteCommands>(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(timeout);
            options.MaxTimeout = TimeSpan.FromSeconds(ceiling);
        }));
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

        Assert.Equal("notGranted", answer.Data()!["result"]!.GetValue<string>());
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

    /// <summary>A refused command said "this session was not granted it" whatever the server's hint, and on a stateless server too.</summary>
    [Fact]
    public async Task WithheldCommand_CarriesTheFleetsHint_WhereTheServerSetNone()
    {
        var granted = new FixedAddress(("tool", "notes_read"), ("tool", "run_program"));
        await using var stand = await ProgramStand.StartAsync(granted, server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"));

        var caught = await stand.RunAsync("try { notes.write({ text: 'x' }); } catch (e) { return e.hint; }");
        var uncaught = await stand.RunAsync("notes.write({ text: 'x' });");

        Assert.Equal(GrantOptions.Hint, caught.Data()!["result"]!.GetValue<string>());
        Assert.Equal((GrantOptions.Hint, GrantOptions.NotGranted), (uncaught.Hint(), Type(uncaught.Text())));
    }

    /// <summary>Snail.MCP.Blender gave its door, its forwarders and its programs one scope hint, written three times; now it is set once.</summary>
    [Fact]
    public async Task WithheldCommand_CarriesTheHintTheServerSetOnce_InItsSubjectsWord()
    {
        var granted = new StatelessAddress(("tool", "notes_read"), ("tool", "run_program"));
        await using var stand = await ProgramStand.StartAsync(
            granted,
            server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"),
            grant => grant.HintOf = (tool, address) => $"ask for {tool} in another {address.Subject}");

        var answer = await stand.RunAsync("try { notes.write({ text: 'x' }); } catch (e) { return [e.type, e.error, e.hint]; }");

        Assert.Equal(
            """["notGranted","'notes_write' was not granted to this request, which may use notes_read, run_program","ask for notes_write in another request"]""",
            answer.Data()!["result"]!.ToJsonString(ToolAnswer.Wire));
    }

    /// <summary>Snail.MCP.AfterEffects has 129 commands and a tool for few: a command that stands for no tool is granted under its own name.</summary>
    [Fact]
    public async Task CommandWithoutATool_IsGrantedUnderItsOwnName()
    {
        var granted = new FixedAddress(("tool", "layer_add"), ("tool", "run_program"));
        await using var stand = await ProgramStand.StartAsync(granted, server =>
        {
            server.Services.AddSingleton<BridgeCommands>();

            return server.WithPrograms<BridgeCommands>(options => options.Global = "ae");
        });

        var answer = await stand.RunAsync("ae.layer_add(); try { ae.layer_remove(); } catch (e) { return e.error; }");

        Assert.Equal("'layer_remove' was not granted to this session, which may use layer_add, run_program", answer.Data()!["result"]!.GetValue<string>());
    }

    /// <summary>Snail.MCP.AfterEffects waited the longest a program may run for every command, since nothing told it the time left.</summary>
    [Fact]
    public async Task Command_IsSentTheTimeItsProgramHasLeft_AndTheRequest()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server =>
        {
            server.Services.AddSingleton<BridgeCommands>();

            return server.WithPrograms<BridgeCommands>(options => options.Global = "ae");
        });

        var answer = await stand.RunAsync("return ae.remaining();", timeoutSeconds: 30);
        var sent = answer.Data()!["result"]!;

        Assert.True(answer.IsOk(), answer.Text());
        Assert.InRange(sent["seconds"]!.GetValue<double>(), 25, 30);
        Assert.True(sent["isAhead"]!.GetValue<bool>());
        Assert.Equal("run_program", sent["tool"]!.GetValue<string>());
    }

    /// <summary>Snail.MCP.Blender counted a program at its first command, so a program that sent none went uncounted.</summary>
    [Fact]
    public async Task Ran_IsCalledOncePerProgram_WithTheRunAndTheServicesOfTheRequest()
    {
        var runs = new List<(ProgramRun Run, int Notes)>();
        void Record(ProgramRun run, IServiceProvider services) => runs.Add((run, services.GetRequiredService<Notes>().Written.Count));
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options =>
        {
            options.Global = "notes";
            options.Ran = Record;
        }));

        await stand.RunAsync("notes.write({ text: 'a' }); return notes.read().count;");
        await stand.RunAsync("return 1;");
        await stand.RunAsync("notes.wirte();");
        await stand.RunAsync("  ");

        Assert.Equal(3, runs.Count);
        Assert.Equal((2, 1, 1), (runs[0].Run.Calls.Count, runs[0].Run.Result!.GetValue<int>(), runs[0].Notes));
        Assert.Empty(runs[1].Run.Calls);
        Assert.Equal(ProgramFailures.NoSuchCommand, runs[2].Run.Failure!.Type);
    }

    [Fact]
    public async Task Description_NamesEveryFieldAFailedCommandThrows()
    {
        await using var stand = await ProgramStand.StartAsync(Everything, server => server.WithPrograms<NoteCommands>(options => options.Global = "notes"));

        var tools = await stand.Client.ListToolsAsync();

        Assert.Contains("throws { error, message, hint, type, details }", tools.Single(tool => tool.Name == "run_program").Description, StringComparison.Ordinal);
    }

    private static string Timeless(string json) => Milliseconds().Replace(json, "\"milliseconds\":0");

    [GeneratedRegex("\"milliseconds\":\\d+")]
    private static partial Regex Milliseconds();

    private static string? Type(string envelope) => System.Text.Json.Nodes.JsonNode.Parse(envelope)?["type"]?.GetValue<string>();
}

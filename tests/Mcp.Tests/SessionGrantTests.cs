using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Testing;
using static Snail.Sdk.Mcp.Tests.Refusals;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The tools a session was granted, read from its address and held at the door and in every forwarder.</summary>
public sealed class SessionGrantTests
{
    [Fact]
    public void Grant_ReadsTheNamedTools_TrimmedEachOnce_BlanksDropped()
    {
        var grant = new SessionGrant(new FixedAddress(("tool", " read_note "), ("tool", "READ_NOTE"), ("tool", " "), ("tool", "list_notes"), ("root", "/w/t")));

        Assert.Equal(["read_note", "list_notes"], grant.All);
    }

    [Fact]
    public void Grant_AllowsWhatItNames_WhateverTheCase_AndNothingElse()
    {
        var grant = new SessionGrant(new FixedAddress(("tool", "read_note")));

        Assert.True(grant.Allows("READ_NOTE"));
        Assert.False(grant.Allows("write_note"));
    }

    [Fact]
    public void Grant_ThatNamesNoTool_AllowsEveryTool()
    {
        var grant = new SessionGrant(new FixedAddress());

        Assert.True(grant.Allows("write_note"));
    }

    [Fact]
    public void Refusal_SaysWhatWasRefused_AndWhatTheSessionMayUse()
    {
        var grant = new SessionGrant(new FixedAddress(("tool", "read_note"), ("tool", "list_notes")));

        Assert.Equal("'write_note' was not granted to this session, which may use read_note, list_notes", grant.Refusal("write_note"));
    }

    [Fact]
    public async Task Call_ToAToolNotGranted_IsRefusedAtTheDoor_InTheGrantsWords()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note")));

        var refused = await stand.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });
        var granted = await stand.Client.CallToolAsync("read_note");

        Assert.True(refused.IsError);
        Assert.Equal("'write_note' was not granted to this session, which may use read_note", refused.Error());
        Assert.Equal(0, stand.Notes.Written);
        Assert.Equal("the note", granted.Text());
    }

    [Fact]
    public async Task List_LeavesOutWhatWasNotGranted()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note"), ("tool", "run_tools")));

        var listed = await stand.Client.ListToolsAsync();

        Assert.Equal(["read_note", "run_tools"], listed.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>A server that wants a withheld tool seen, so the model is told why rather than inventing a way round it.</summary>
    [Fact]
    public async Task List_WhenWithheldToolsAreShown_KeepsThem_AndTheCallIsStillRefused()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note")), options => options.HidesWithheldTools = false);

        var listed = await stand.Client.ListToolsAsync();
        var refused = await stand.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.Contains(listed, tool => tool.Name == "write_note");
        Assert.True(refused.IsError);
    }

    [Fact]
    public async Task Session_ThatNamesNoTool_UsesEveryTool()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress());

        var written = await stand.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.NotEqual(true, written.IsError);
        Assert.Equal(1, stand.Notes.Written);
    }

    [Fact]
    public async Task Session_ThatNamesNoTool_WhereAGrantIsRequired_IsRefusedEveryTool_AndListedNone()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("root", "/w/t")), options => options.RequiresGrant = true);

        var refused = await stand.Client.CallToolAsync("read_note");
        var listed = await stand.Client.ListToolsAsync();

        Assert.True(refused.IsError);
        Assert.StartsWith("'read_note' was not granted to this session, which was granted no tool", refused.Error(), StringComparison.Ordinal);
        Assert.Empty(listed);
    }

    /// <summary>A program over stdio has nowhere to name a tool, so a required grant never shuts it out.</summary>
    [Fact]
    public async Task Program_WithoutAnAddress_WhereAGrantIsRequired_StillUsesEveryTool()
    {
        await using var stand = await SessionStand.StartAsync(null, options => options.RequiresGrant = true);

        var read = await stand.Client.CallToolAsync("read_note");

        Assert.Equal("the note", read.Text());
    }

    [Fact]
    public async Task Refusal_ShapedByTheServer_IsTheAnswerOfARefusedCall()
    {
        await using var stand = await SessionStand.StartAsync(
            new FixedAddress(("tool", "read_note")),
            options => options.Refusal = (tool, words) => new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = $$"""{"ok":false,"tool":"{{tool}}","error":"{{words}}"}""" }] });

        var refused = await stand.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.StartsWith("""{"ok":false,"tool":"write_note","error":"'write_note' was not granted""", refused.Text(), StringComparison.Ordinal);
    }

    /// <summary>A server with scopes of its own registers its gate, and the door asks it instead of the grant.</summary>
    [Fact]
    public async Task Gate_OfTheServersOwn_DecidesInsteadOfTheGrant()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(), gate: new ReadOnlyGate());

        var refused = await stand.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.Equal("this session only reads", refused.Error());
    }

    /// <summary>A stateless server has no session: "not granted to this session" told its caller about one it never had.</summary>
    [Fact]
    public void Refusal_SpeaksOfTheRequest_WhereTheAddressSpeaksForOne()
    {
        var named = new SessionGrant(new StatelessAddress(("tool", "read_note")));
        var none = new SessionGrant(new StatelessAddress());

        Assert.Equal("'write_note' was not granted to this request, which may use read_note", named.Refusal("write_note"));
        Assert.Equal("'write_note' was not granted to this request, which was granted no tool: name each tool it may use in the address, ?tool=<name>", none.Refusal("write_note"));
    }

    /// <summary>The hint reads as the hints beside it do, and names no session a stateless server would not have.</summary>
    [Fact]
    public async Task Call_RefusedAtTheDoor_CarriesTheTypeAndTheFleetsHint()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note")));

        var refused = await stand.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.Equal(GrantOptions.Hint, refused.Hint());
        Assert.Equal(GrantOptions.NotGranted, TypeOf(refused));
        Assert.True(char.IsLower(GrantOptions.Hint[0]));
        Assert.False(GrantOptions.Hint.EndsWith('.'));
        Assert.DoesNotContain("session", GrantOptions.Hint, StringComparison.Ordinal);
    }

    /// <summary>Snail.MCP.Blender set a whole <see cref="GrantOptions.Refusal"/> only to give the door its own hint.</summary>
    [Fact]
    public async Task Hint_SetOnce_IsTheDoorsHint_AndKnowsWhatTheRefusedOneIs()
    {
        await using var session = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note")), options => options.HintOf = Hinted);
        await using var request = await SessionStand.StartAsync(new StatelessAddress(("tool", "read_note")), options => options.HintOf = Hinted);

        var bySession = await session.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });
        var byRequest = await request.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.Equal(("ask for write_note in another session", GrantOptions.NotGranted), (bySession.Hint(), TypeOf(bySession)));
        Assert.Equal("ask for write_note in another request", byRequest.Hint());
    }

    /// <summary>A server that shapes the whole refusal keeps its shape; the hint it was given is read where it asks for it.</summary>
    [Fact]
    public async Task Hint_BesideARefusalOfTheServersShape_LeavesTheShapeAlone()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note")), options =>
        {
            options.HintOf = Hinted;
            options.Refusal = (tool, words) => new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = $"{tool}: {words}" }] };
        });

        var refused = await stand.Client.CallToolAsync("write_note", new Dictionary<string, object?> { ["text"] = "x" });

        Assert.Equal("write_note: 'write_note' was not granted to this session, which may use read_note", refused.Text());
    }

    internal static string Hinted(string tool, ISessionAddress address) => $"ask for {tool} in another {address.Subject}";

    private sealed class ReadOnlyGate : IToolGate
    {
        public string? Refusal(string tool, IServiceProvider services) =>
            tool.StartsWith("write", StringComparison.Ordinal) ? "this session only reads" : null;
    }
}

/// <summary>A forwarder's steps held to the same grant as the door, and no forwarder run by another.</summary>
public sealed class ToolForwardingTests
{
    [Fact]
    public async Task Forwarder_RunsAGrantedStep_AsTheProtocolWould()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note"), ("tool", "run_tools")));

        var ran = await stand.Client.CallToolAsync("run_tools", new Dictionary<string, object?> { ["step"] = "read_note" });

        Assert.Equal("the note", ran.Text());
    }

    /// <summary>A role granted read_note and run_tools wrote through run_tools in three servers of the fleet, until each checked the grant itself.</summary>
    [Fact]
    public async Task Forwarder_RefusesAStepNotGranted_AndTheStepNeverRuns()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "read_note"), ("tool", "run_tools")));

        var refused = await stand.Client.CallToolAsync("run_tools", new Dictionary<string, object?> { ["step"] = "write_note", ["text"] = "x" });

        Assert.True(refused.IsError);
        Assert.Equal("'write_note' was not granted to this session, which may use read_note, run_tools", refused.Error());
        Assert.Equal(0, stand.Notes.Written);
    }

    [Fact]
    public async Task Forwarder_RefusesAnotherForwarderAsItsStep()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress());

        var refused = await stand.Client.CallToolAsync("run_tools", new Dictionary<string, object?> { ["step"] = "run_program" });

        Assert.True(refused.IsError);
        Assert.Equal("'run_program' runs other tools itself, so 'run_tools' does not run it", refused.Error());
    }

    /// <summary>A step that forwards without saying so is caught at its first call, so a forgotten mark does not open the hole.</summary>
    [Fact]
    public async Task Step_ThatForwardsUnmarked_IsRefusedItsOwnStep()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress());

        var refused = await stand.Client.CallToolAsync("run_tools", new Dictionary<string, object?> { ["step"] = "relay" });

        Assert.Equal("'relay' was itself run by a forwarder, and a step runs no other tool", refused.Error());
    }

    [Fact]
    public async Task Forwarder_CalledByTheClient_ForwardsAgain_AfterAStepWasRefused()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress());

        await stand.Client.CallToolAsync("run_tools", new Dictionary<string, object?> { ["step"] = "relay" });
        var ran = await stand.Client.CallToolAsync("relay");

        Assert.Equal("the note", ran.Text());
    }

    [Fact]
    public async Task Refusal_OfACommandThatIsNoTool_AsksTheSameGate()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "git_status"), ("tool", "run_commands")));

        var refused = await stand.Client.CallToolAsync("run_commands", new Dictionary<string, object?> { ["command"] = "push" });
        var allowed = await stand.Client.CallToolAsync("run_commands", new Dictionary<string, object?> { ["command"] = "status" });

        Assert.Equal("'git_push' was not granted to this session, which may use git_status, run_commands", refused.Text());
        Assert.Equal("ran status", allowed.Text());
    }

    /// <summary>Snail.MCP.Git copied the type and the hint to answer a refused command as the door answers a refused tool.</summary>
    [Fact]
    public async Task Refused_OfACommandThatIsNoTool_IsTheDoorsAnswer()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "git_status"), ("tool", "run_catalog")));

        var refused = await stand.Client.CallToolAsync("run_catalog", new Dictionary<string, object?> { ["command"] = "push" });
        var allowed = await stand.Client.CallToolAsync("run_catalog", new Dictionary<string, object?> { ["command"] = "status" });

        Assert.True(refused.IsError);
        Assert.Equal("'git_push' was not granted to this session, which may use git_status, run_catalog", refused.Error());
        Assert.Equal((GrantOptions.Hint, GrantOptions.NotGranted), (refused.Hint(), TypeOf(refused)));
        Assert.Equal("ran status", allowed.Text());
    }

    [Fact]
    public async Task Refused_OfACommandThatIsNoTool_TakesTheShapeTheServerGaveTheDoor()
    {
        await using var stand = await SessionStand.StartAsync(
            new FixedAddress(("tool", "run_catalog")),
            options => options.Refusal = (tool, words) => new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = $"{tool}: {words}" }] });

        var refused = await stand.Client.CallToolAsync("run_catalog", new Dictionary<string, object?> { ["command"] = "push" });

        Assert.StartsWith("git_push: 'git_push' was not granted", refused.Text(), StringComparison.Ordinal);
    }

    /// <summary>Snail.MCP.Git kept a hint of its own for its catalog; set once, the hint is the catalog's as it is the door's.</summary>
    [Fact]
    public async Task Refused_OfACommandThatIsNoTool_CarriesTheHintTheServerSetOnce()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "run_catalog"), ("tool", "run_answers")), options => options.HintOf = SessionGrantTests.Hinted);

        var refused = await stand.Client.CallToolAsync("run_catalog", new Dictionary<string, object?> { ["command"] = "push" });
        var answered = await stand.Client.CallToolAsync("run_answers", new Dictionary<string, object?> { ["command"] = "push" });

        Assert.Equal("ask for git_push in another session", refused.Hint());
        Assert.EndsWith("|ask for git_push in another session|notGranted", answered.Text(), StringComparison.Ordinal);
    }

    /// <summary>A program branching on the type met <c>notGranted</c> from the catalog and <c>refused</c> from its runner: one refusal, one word.</summary>
    [Fact]
    public async Task Refused_AsAnAnswer_OfACommandThatIsNoTool_CarriesTheDoorsTypeAndHint()
    {
        await using var stand = await SessionStand.StartAsync(new FixedAddress(("tool", "run_answers")));

        var refused = await stand.Client.CallToolAsync("run_answers", new Dictionary<string, object?> { ["command"] = "push" });
        await using var ungated = await SessionStand.StartAsync(new FixedAddress());
        var allowed = await ungated.Client.CallToolAsync("run_answers", new Dictionary<string, object?> { ["command"] = "push" });

        Assert.Equal($"'git_push' was not granted to this session, which may use run_answers|{GrantOptions.Hint}|{GrantOptions.NotGranted}", refused.Text());
        Assert.Equal("ran push", allowed.Text());
    }
}

/// <summary>The questions of <see cref="ToolForwarding"/> asked of a container, with no request: a forwarder handed services, a unit test.</summary>
public sealed class ToolForwardingWithoutARequestTests
{
    [Fact]
    public void Refusal_FromServices_AsksTheSameGate()
    {
        using var services = Services(new FixedAddress(("tool", "read_note")));

        Assert.Null(ToolForwarding.Refusal(services, "read_note"));
        Assert.Equal("'write_note' was not granted to this session, which may use read_note", ToolForwarding.Refusal(services, "write_note"));
    }

    [Fact]
    public void Refused_FromServices_IsTheDoorsAnswer_WithTheServersHint()
    {
        using var services = Services(new StatelessAddress(("tool", "read_note")), options => options.HintOf = SessionGrantTests.Hinted);

        var refused = ToolForwarding.Refused(services, "write_note")!;
        var answer = ToolForwarding.Refused<JsonNode>(services, "write_note")!;

        Assert.True(refused.IsError);
        Assert.Equal(("'write_note' was not granted to this request, which may use read_note", "ask for write_note in another request", GrantOptions.NotGranted), (refused.Error(), refused.Hint(), TypeOf(refused)));
        Assert.Equal(("ask for write_note in another request", GrantOptions.NotGranted), (answer.Hint, answer.Type));
        Assert.Null(ToolForwarding.Refused(services, "read_note"));
        Assert.Null(ToolForwarding.Refused<JsonNode>(services, "read_note"));
    }

    [Fact]
    public void Hint_IsTheServersOne_AndTheFleetsWhereItSetNone()
    {
        using var hinted = Services(new FixedAddress(), options => options.HintOf = SessionGrantTests.Hinted);
        using var plain = Services(new FixedAddress());
        using var ungranted = new ServiceCollection().BuildServiceProvider();

        Assert.Equal("ask for git_push in another session", ToolForwarding.Hint(hinted, "git_push"));
        Assert.Equal(GrantOptions.Hint, ToolForwarding.Hint(plain, "git_push"));
        Assert.Equal(GrantOptions.Hint, ToolForwarding.Hint(ungranted, "git_push"));
    }

    /// <summary>A server that holds no grant refuses nothing, with or without a request.</summary>
    [Fact]
    public void Refusal_FromServicesWithoutAGate_IsNone()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        Assert.Null(ToolForwarding.Refusal(services, "write_note"));
        Assert.Null(ToolForwarding.Refused(services, "write_note"));
    }

    private static ServiceProvider Services(ISessionAddress address, Action<GrantOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(address);
        services.AddMcpServer().RefuseWhatWasNotGranted(configure);

        return services.BuildServiceProvider();
    }
}

/// <summary>A server of notes over a pair of pipes, its sessions at a fixed address, and the client connected to it.</summary>
internal sealed class SessionStand : IAsyncDisposable
{
    private SessionStand(IHost host, McpClient client, Notes notes)
    {
        Host = host;
        Client = client;
        Notes = notes;
    }

    public IHost Host { get; }

    public McpClient Client { get; }

    public Notes Notes { get; }

    /// <summary>A server whose sessions are at <paramref name="address"/>, or have none when it is null — a program over stdio.</summary>
    public static async Task<SessionStand> StartAsync(ISessionAddress? address, Action<GrantOptions>? configure = null, IToolGate? gate = null)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var notes = new Notes();
        var builder = McpHost.CreateBuilder([]);

        builder.Services.AddSingleton(notes);

        if (address is not null)
            builder.Services.AddSingleton(address);

        if (gate is not null)
            builder.Services.AddSingleton(gate);

        builder.Services.AddMcpServer()
            .WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream())
            .RefuseWhatWasNotGranted(configure)
            .WithTools<NoteTools>();

        var host = builder.Build();
        await host.StartAsync();
        var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));

        return new SessionStand(host, client, notes);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Host.StopAsync();
        Host.Dispose();
    }
}

/// <summary>What the sample server's tools changed, for a test to see.</summary>
internal sealed class Notes
{
    private int _written;

    public int Written => _written;

    public void Write() => Interlocked.Increment(ref _written);
}

[McpServerToolType]
internal sealed class NoteTools
{
    [McpServerTool(Name = "read_note", ReadOnly = true), Description("Reads the note.")]
    public static string ReadNote() => "the note";

    [McpServerTool(Name = "write_note", Destructive = true), Description("Writes the note.")]
    public static string WriteNote(Notes notes, [Description("What to write.")] string text)
    {
        notes.Write();

        return $"wrote {text}";
    }

    [McpServerTool(Name = "run_tools", Destructive = true), Description("Runs one tool of this server."), Forwards]
    public static async Task<CallToolResult> RunTools(RequestContext<CallToolRequestParams> context, [Description("The tool.")] string step, [Description("Its text.")] string? text = null, CancellationToken cancellationToken = default) =>
        await Forward(context, step, text is null ? null : new JsonObject { ["text"] = text }, cancellationToken);

    [McpServerTool(Name = "run_program", Destructive = true), Description("Runs a program of this server's tools."), Forwards]
    public static async Task<CallToolResult> RunProgram(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default) =>
        await Forward(context, "read_note", null, cancellationToken);

    [McpServerTool(Name = "relay", ReadOnly = true), Description("Reads the note through another tool, unmarked.")]
    public static async Task<CallToolResult> Relay(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken = default) =>
        await Forward(context, "read_note", null, cancellationToken);

    [McpServerTool(Name = "run_commands", Destructive = true), Description("Runs a command, granted under its tool's name.")]
    public static string RunCommands(RequestContext<CallToolRequestParams> context, [Description("The command.")] string command) =>
        ToolForwarding.Refusal(context, $"git_{command}") ?? $"ran {command}";

    [McpServerTool(Name = "run_catalog", Destructive = true), Description("Runs a command, answered as the door answers a tool.")]
    public static CallToolResult RunCatalog(RequestContext<CallToolRequestParams> context, [Description("The command.")] string command) =>
        ToolForwarding.Refused(context, $"git_{command}") ?? new CallToolResult { Content = [new TextContentBlock { Text = $"ran {command}" }] };

    [McpServerTool(Name = "run_answers", Destructive = true), Description("Runs a command whose answer is an Answer, and says its failure.")]
    public static string RunAnswers(RequestContext<CallToolRequestParams> context, [Description("The command.")] string command) =>
        ToolForwarding.Refused<JsonNode>(context, $"git_{command}") is { } refused ? $"{refused.Error}|{refused.Hint}|{refused.Type}" : $"ran {command}";

    private static async Task<CallToolResult> Forward(RequestContext<CallToolRequestParams> context, string step, JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (!context.Server.ServerOptions.ToolCollection!.TryGetPrimitive(step, out var tool))
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = $"no tool {step}" }] };

        return await ToolForwarding.CallAsync(context, tool, arguments, cancellationToken);
    }
}

/// <summary>What the sample tests read of an answer beyond its words.</summary>
internal static class Refusals
{
    /// <summary>The type the envelope of the answer carries.</summary>
    public static string? TypeOf(CallToolResult answer) => JsonNode.Parse(answer.Text())?["type"]?.GetValue<string>();
}

/// <summary>An address that speaks for one request, as the address of a stateless transport does.</summary>
internal sealed class StatelessAddress(params (string Parameter, string Value)[] values) : ISessionAddress
{
    private readonly FixedAddress _fixed = new(values);

    public string Subject => "request";

    public IReadOnlyList<string> Values(string parameter) => _fixed.Values(parameter);
}

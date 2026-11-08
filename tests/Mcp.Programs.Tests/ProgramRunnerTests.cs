using System.Diagnostics;
using System.Text.Json.Nodes;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Programs.Tests;

/// <summary>A program over a catalog: what it returns, logs and sends, and every bound that stops it, each in its own words.</summary>
public sealed class ProgramRunnerTests
{
    private readonly Catalog _catalog = new();

    [Fact]
    public async Task Program_CallingCommandsInALoop_ReturnsItsResultAndEveryCall()
    {
        var run = await RunAsync("""
            let n = 0;
            for (let i = 0; i < 5; i++) {
                n = git.count({ n }).n;
            }
            return { n, clean: git.status().clean };
            """);

        Assert.Null(run.Failure);
        Assert.Equal(5, run.Result!["n"]!.GetValue<int>());
        Assert.True(run.Result["clean"]!.GetValue<bool>());
        Assert.Equal(["count", "count", "count", "count", "count", "status"], run.Calls.Select(call => call.Command));
        Assert.All(run.Calls, call => Assert.True(call.IsOk));
    }

    [Fact]
    public async Task Program_ReadsItsInput()
    {
        var run = await RunAsync("return input.names.length;", input: new JsonObject { ["names"] = new JsonArray("a", "b", "c") });

        Assert.Equal(3, run.Result!.GetValue<int>());
    }

    [Fact]
    public async Task Program_ThatRecursesForever_StopsAtTheDepth_InItsOwnWords()
    {
        var run = await RunAsync("function down(n) { return down(n + 1); }\nreturn down(0);");

        Assert.Equal(ProgramFailures.ProgramTooLarge, run.Failure!.Type);
        Assert.Equal("the program nested its calls deeper than 256 levels", run.Failure.Error);
        Assert.Contains("loop", run.Failure.Hint, StringComparison.Ordinal);
    }

    /// <summary>An empty busy loop meets the statement bound in tens of milliseconds, long before any timeout.</summary>
    [Fact]
    public async Task Program_ThatLoopsForever_StopsAtTheStatementCount_LongBeforeItsTimeout()
    {
        var watch = Stopwatch.StartNew();

        var run = await RunAsync("while (true) {}");

        Assert.Equal(ProgramFailures.ProgramTooLarge, run.Failure!.Type);
        Assert.Equal("the program ran more than 1000000 statements", run.Failure.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Program_ThatHoldsTooMuch_StopsAtTheMemory_InItsOwnWords()
    {
        var run = await RunAsync("const held = []; for (let i = 0; i < 100000; i++) { held.push('x'.repeat(10000) + i); }\nreturn held.length;");

        Assert.Equal(ProgramFailures.ProgramTooLarge, run.Failure!.Type);
        Assert.Equal("the program used more than the 32 MB of memory a program may use", run.Failure.Error);
    }

    [Fact]
    public async Task Program_SendingMoreCommandsThanAllowed_StopsAtTheLimit_WithWhatItSent()
    {
        var run = await RunAsync("for (let i = 0; i < 10; i++) { git.status(); }", options => options.MaxCalls = 3);

        Assert.Equal(ProgramFailures.TooManyCalls, run.Failure!.Type);
        Assert.Equal("a program sends at most 3 commands; this one asked for more", run.Failure.Error);
        Assert.Equal(3, run.Calls.Count);
        Assert.Equal(3, _catalog.Sent);
    }

    /// <summary>The time is spent in a command, where the engine's own checks never run: the program is given up on at its deadline all the same.</summary>
    [Fact]
    public async Task Program_SpendingItsTimeInSlowCommands_TimesOut_AtItsDeadline()
    {
        var watch = Stopwatch.StartNew();

        var run = await RunAsync("git.status();\nfor (let i = 0; i < 10; i++) { git.slow(); }", timeout: TimeSpan.FromMilliseconds(500));

        Assert.Equal(ProgramFailures.Timeout, run.Failure!.Type);
        Assert.Equal("the program ran longer than the 0.5 seconds it was given", run.Failure.Error);
        Assert.StartsWith("the commands it had already sent stand", run.Failure.Hint, StringComparison.Ordinal);
        Assert.Equal([true, false], run.Calls.Select(call => call.IsOk));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Program_WhoseCallIsCancelled_ReportsCancelled_NotATimeout()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var run = await RunAsync("git.slow();", cancellationToken: cancel.Token);

        Assert.Equal(ProgramFailures.Cancelled, run.Failure!.Type);
        Assert.Equal("the call that ran the program was cancelled", run.Failure.Error);
    }

    [Fact]
    public async Task UnknownCommand_StopsTheProgram_WithTheNearestNames()
    {
        var run = await RunAsync("try { git.stauts(); } catch (e) { return 'caught'; }");

        Assert.Equal(ProgramFailures.NoSuchCommand, run.Failure!.Type);
        Assert.Equal("no command 'stauts'; the nearest: status", run.Failure.Error);
        Assert.Equal("git has the commands count, fail, log, remove_layer, slow, status; call one of them by its name", run.Failure.Hint);
        Assert.Empty(run.Calls);
    }

    /// <summary>The words of a tool turned round — layers_remove for remove_layer — share their words with the command.</summary>
    [Fact]
    public async Task UnknownCommand_WithTheWordsTurnedRound_NamesTheCommandOfThoseWords()
    {
        var run = await RunAsync("git.layers_remove();");

        Assert.Equal("no command 'layers_remove'; the nearest: remove_layer", run.Failure!.Error);
    }

    [Fact]
    public async Task CommandsObject_ReadAsJsonOrAwaited_DoesNotCountAsAMiss()
    {
        var run = await RunAsync("return { keys: Object.keys(git), json: JSON.stringify(git).length > 0, then: typeof git.then };");

        Assert.Null(run.Failure);
        Assert.Equal("undefined", run.Result!["then"]!.GetValue<string>());
        Assert.Equal(6, run.Result["keys"]!.AsArray().Count);
    }

    [Fact]
    public async Task RefusedCommand_ThrowsAnError_ThatTheProgramCatches()
    {
        var run = await RunAsync(
            "try { git.status(); return 'ran'; } catch (e) { return [e.type, e.error, e.message === e.error, typeof e.hint]; }",
            refusal: tool => tool == "git_status" ? "'git_status' was not granted to this session" : null);

        Assert.Null(run.Failure);
        Assert.Equal("""["notGranted","'git_status' was not granted to this session",true,"string"]""", run.Result!.ToJsonString(ToolAnswer.Wire));
        Assert.Equal(0, _catalog.Sent);
        Assert.False(Assert.Single(run.Calls).IsOk);
    }

    [Fact]
    public async Task RefusedCommand_Uncaught_StopsTheProgram_AsNotGranted_AtItsLine()
    {
        var run = await RunAsync("git.count();\ngit.status();", refusal: tool => tool == "git_status" ? "not granted" : null);

        Assert.Equal(ProgramFailures.NotGranted, run.Failure!.Type);
        Assert.Equal("the program stopped at line 2: not granted", run.Failure.Error);
    }

    /// <summary>Without a request there is no server to ask for its hint: the fleet's, which names no session.</summary>
    [Fact]
    public async Task RefusedCommand_WithoutARequest_CarriesTheFleetsHint()
    {
        var run = await RunAsync("git.status();", refusal: tool => tool == "git_status" ? "not granted" : null);

        Assert.Equal(GrantOptions.Hint, run.Failure!.Hint);
    }

    /// <summary>A command that stands for no tool is asked of the gate under its own name, never let past it.</summary>
    [Fact]
    public async Task CommandWithoutATool_IsAskedOfTheGate_UnderItsOwnName()
    {
        var asked = new List<string>();

        var run = await RunAsync(
            "tools.status(); return 'ran';",
            options => options.Global = "tools",
            refusal: tool =>
            {
                asked.Add(tool);

                return null;
            },
            commands: new Named(["status"]));

        Assert.Equal("ran", run.Result!.GetValue<string>());
        Assert.Equal(["status"], asked);
    }

    [Fact]
    public async Task Command_IsSentItsDeadline_AndNoRequestWhereTheRunnerWasGivenNone()
    {
        var sent = new List<CommandCall>();

        var run = await RunAsync("tools.status(); return 'ran';", options => options.Global = "tools", timeout: TimeSpan.FromSeconds(20), commands: new Named(["status"], sent));
        var call = Assert.Single(sent);

        Assert.Null(run.Failure);
        Assert.Equal(("status", null), (call.Command, call.Request));
        Assert.InRange(call.Deadline - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
        Assert.InRange(call.Remaining, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.Zero, (call with { Deadline = DateTimeOffset.UtcNow.AddSeconds(-1) }).Remaining);
    }

    [Fact]
    public async Task FailedCommand_Uncaught_KeepsItsOwnTypeAndDetails()
    {
        var run = await RunAsync("git.fail();");

        Assert.Equal("NotFound", run.Failure!.Type);
        Assert.Equal("the program stopped at line 1: the branch is gone", run.Failure.Error);
        Assert.Equal("list the branches first", run.Failure.Hint);
        Assert.Equal("""{"branch":"main"}""", run.Failure.Details!.ToJsonString());
    }

    [Fact]
    public async Task FailedCommand_Caught_CarriesItsDetails()
    {
        var run = await RunAsync("try { git.fail(); } catch (e) { return e.details.branch; }");

        Assert.Equal("main", run.Result!.GetValue<string>());
    }

    /// <summary>With log on the commands' object, git.log wrote a line instead of reading the history, silently (Snail.MCP.Git).</summary>
    [Fact]
    public async Task Log_IsTheProgramsOwn_AndACommandNamedLogStillRuns()
    {
        var run = await RunAsync("log('reading'); return git.log().history;");

        Assert.Equal("""["a1","b2"]""", run.Result!.ToJsonString());
        Assert.Equal(["reading"], run.Log);
        Assert.Equal("log", Assert.Single(run.Calls).Command);
    }

    [Fact]
    public async Task Log_IsNotPutOnTheCommandsObject()
    {
        var run = await RunAsync("return ['log' in tools, typeof log];", options => options.Global = "tools", commands: new Named(["status"]));

        Assert.Equal("""[false,"function"]""", run.Result!.ToJsonString());
    }

    [Fact]
    public async Task Log_WritesObjectsAsJson_CutsLongLines_AndKeepsItsLimit()
    {
        var run = await RunAsync(
            "log('state', { clean: true }, [1, 2]); log('y'.repeat(50)); for (let i = 0; i < 10; i++) { log(i); }",
            options =>
            {
                options.LogLines = 4;
                options.LogLineLength = 20;
            });

        Assert.Equal(["state {\"clean\":true}…", $"{new string('y', 20)}…", "0", "1"], run.Log);
    }

    [Fact]
    public async Task Program_ThatIsNotJavaScript_ReportsSyntax_AtTheLineOfTheProgram()
    {
        var run = await RunAsync("const a = 1;\nconst = 2;");

        Assert.Equal(ProgramFailures.Syntax, run.Failure!.Type);
        Assert.StartsWith("the program is not valid JavaScript at line 2", run.Failure.Error, StringComparison.Ordinal);
        Assert.Empty(run.Calls);
    }

    [Fact]
    public async Task Program_ThatThrows_ReportsRuntime_AtTheLineOfTheProgram()
    {
        var run = await RunAsync("git.status();\nconst gone = null;\nreturn gone.name;");

        Assert.Equal(ProgramFailures.Runtime, run.Failure!.Type);
        Assert.StartsWith("the program stopped at line 3", run.Failure.Error, StringComparison.Ordinal);
        Assert.Single(run.Calls);
    }

    [Fact]
    public async Task Command_GivenSomethingOtherThanOneObject_StopsTheProgram_BeforeItRuns()
    {
        var run = await RunAsync("git.status('main');");

        Assert.Equal(ProgramFailures.Invalid, run.Failure!.Type);
        Assert.Equal("git.status takes one object of arguments, and was given string", run.Failure.Error);
        Assert.Equal(0, _catalog.Sent);
    }

    [Fact]
    public async Task Command_ReceivesTheObjectItWasGiven()
    {
        var run = await RunAsync("return git.status({ path: 'src', deep: true }).given;");

        Assert.Equal("""{"path":"src","deep":true}""", run.Result!.ToJsonString());
    }

    private Task<ProgramRun> RunAsync(
        string code,
        Action<ProgramOptions>? configure = null,
        JsonObject? input = null,
        TimeSpan? timeout = null,
        Func<string, string?>? refusal = null,
        IProgramCommands? commands = null,
        CancellationToken cancellationToken = default)
    {
        var options = new ProgramOptions { Global = "git" };
        configure?.Invoke(options);

        return new ProgramRunner(options).RunAsync(commands ?? _catalog, code, input, timeout, refusal, cancellationToken: cancellationToken);
    }

    /// <summary>Commands that stand for no tool, and keep what each was sent with.</summary>
    private sealed class Named(IReadOnlyList<string> names, List<CommandCall>? sent = null) : IProgramCommands
    {
        public IReadOnlyList<string> Names => names;

        public string? ToolOf(string command) => null;

        public Task<ProgramAnswer> CallAsync(string command, JsonObject arguments, CancellationToken cancellationToken) =>
            Task.FromResult(ProgramAnswer.Ok(null));

        public Task<ProgramAnswer> CallAsync(CommandCall call, CancellationToken cancellationToken)
        {
            sent?.Add(call);

            return Task.FromResult(ProgramAnswer.Ok(null));
        }
    }
}

using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>The tool <c>run_program</c>: a program over the server's commands, answered in the envelope with what it returned, logged and sent.</summary>
internal sealed class ProgramTool(ProgramOptions options, ProgramRunner runner, Func<RequestContext<CallToolRequestParams>, IProgramCommands> commandsOf)
{
    public const string Name = "run_program";

    public const string Code = "The body of the program: JavaScript in strict mode, with return for whatever should come back as result.";

    public const string Input = "Values the program reads as input, so that the code itself can stay the same between calls.";

    public const string TimeoutSeconds = "Seconds the program may run; the description of run_program states the default and the most it may be given.";

    /// <summary>The description a model reads: the server's own sentence or a general one, then the rules and limits every program keeps.</summary>
    public string Description
    {
        get
        {
            var global = options.Global;
            var purpose = options.Description ?? $"""
                Run a JavaScript program whose functions are this server's commands, {global}.<command>({"{"} … {"}"}), with loops,
                conditions and the results in hand. Use it when the work decides as it goes or takes many steps: the steps run
                here, and only what the program returns comes back, instead of a call and a reply per step.
                """;
            var rules = $"""
                What the program returns comes back as result, log(…) writes lines that come back with it, and input is the object
                passed alongside. A failed or refused command throws {"{"} error, message, hint, type, details {"}"}, which the program may
                catch and act on; a command the server does not have stops the program with the names nearest to it. The program
                reaches nothing but the commands: no files, no network, no clock but its own. The reply lists every command it sent.
                """;

            return $"{purpose.Trim()}\n{rules}\n{options.Limits}";
        }
    }

    [Forwards]
    public async Task<CallToolResult> RunAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(Code)] string code,
        [Description(Input)] JsonObject? input = null,
        [Description(TimeoutSeconds)] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var commands = commandsOf(context);

        if (string.IsNullOrWhiteSpace(code))
            return ToolAnswer.Failed("a program needs code", $"the program calls {options.Global}.<command>({{ … }}); the commands are {string.Join(", ", commands.Names)}", ProgramFailures.Invalid);

        var most = ProgramOptions.Seconds(options.MaxTimeout);

        if (timeoutSeconds is < 1 || timeoutSeconds > most)
            return ToolAnswer.Failed($"timeoutSeconds is {timeoutSeconds}, and a program is given 1 to {most} seconds", $"leave it out for the default of {ProgramOptions.Seconds(options.Timeout)}", ProgramFailures.Invalid);

        var timeout = timeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : options.Timeout;
        var run = await runner.RunAsync(commands, code, input, timeout, tool => ToolForwarding.Refusal(context, tool), context, cancellationToken).ConfigureAwait(false);
        var data = Data(run);

        options.Ran?.Invoke(run, (context.Services ?? context.Server.Services)!);

        return run.Failure is { } failure ? ToolAnswer.Failed(failure.Error, failure.Hint, failure.Type, data) : ToolAnswer.Ok(data);
    }

    /// <summary>What the answer carries: the result within its budget, the log, every command sent, and what a failed command reported.</summary>
    private JsonObject Data(ProgramRun run)
    {
        var size = run.Result is { } result ? Encoding.UTF8.GetByteCount(result.ToJsonString(ToolAnswer.Wire)) : 0;
        var isOmitted = size > options.ResultBytes;
        var data = new JsonObject
        {
            ["result"] = isOmitted ? null : run.Result?.DeepClone(),
            ["log"] = new JsonArray([.. run.Log.Select(line => (JsonNode?)JsonValue.Create(line))]),
            ["calls"] = new JsonArray([.. run.Calls.Select(Call)]),
        };

        if (isOmitted)
        {
            data["isOmitted"] = true;
            data["note"] = $"the result was {size} bytes, more than the {options.ResultBytes} an answer may carry; return what matters";
        }

        if (run.Failure?.Details is { } details)
            data["details"] = details.DeepClone();

        return data;
    }

    private static JsonNode? Call(ProgramCall call) =>
        new JsonObject { ["command"] = call.Command, ["ok"] = call.IsOk, ["milliseconds"] = call.Milliseconds };
}

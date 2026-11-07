using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>The tool <c>run_tools</c>: several tools of the server in one call, in order, stopping at the first that fails.</summary>
/// <remarks>
/// <para>
/// Work is rarely one question: load, ask for the errors, read the file they point at. Each is a round trip through the
/// model, and each answer rides back into the window whether the next step needed it or not.
/// </para>
/// <para>
/// The steps are the server's own tools, run through <see cref="ToolForwarding.CallAsync"/> — the same objects the
/// protocol runs, under the same grant, and never a forwarder inside another. A tool added tomorrow is a step today.
/// A step's answer is unwrapped from its envelope, so the answer is one envelope with each step's data inside, not
/// envelopes inside an envelope.
/// </para>
/// <para>
/// A step of a skill the session has not loaded runs without loading it: the step was found by <c>find_tool</c>, and
/// loading it would change a tool list the client would then read again.
/// </para>
/// </remarks>
internal static class RunTools
{
    public const string Name = "run_tools";

    public static McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(RunAsync, new McpServerToolCreateOptions { Services = services, SerializerOptions = DiscoveryWire.Tools });

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = true, OpenWorld = true)]
    [Description(ToolDescriptions.RunTools)]
    [Forwards]
    private static async Task<CallToolResult> RunAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.Steps)] ToolStep[] steps,
        CancellationToken cancellationToken = default)
    {
        var services = SessionRegistration.ServicesOf(context)!;
        var options = services.GetRequiredService<SequenceOptions>();
        var limit = Math.Max(1, options.MaxSteps);

        if (steps.Length == 0)
            return ToolAnswer.Failed("name the steps to run", """a step is {"tool": "<name>", "arguments": {…}}""", ArgumentCheck.Type);

        if (steps.Length > limit)
            return ToolAnswer.Failed($"a run takes at most {limit} steps, not {steps.Length}", "split the work into several calls", ArgumentCheck.Type);

        var run = new Run(Math.Max(0, options.OutputBudget));

        for (var at = 0; at < steps.Length; at++)
        {
            var step = await StepAsync(context, steps[at], cancellationToken).ConfigureAwait(false);

            run.Add(step);

            if (!step.IsOk)
                return ToolAnswer.Failed($"step {at + 1} of {steps.Length} ({step.Tool}) failed: {step.Error}", step.Hint, step.Type, run.Report());
        }

        return ToolAnswer.Ok(run.Report());
    }

    /// <summary>One step, run or refused.</summary>
    private static async Task<Step> StepAsync(RequestContext<CallToolRequestParams> context, ToolStep step, CancellationToken cancellationToken)
    {
        var services = SessionRegistration.ServicesOf(context)!;
        var named = step.Tool?.Trim() ?? string.Empty;

        if (ServerTools.Named(context.Server, named) is not { } tool)
            return Step.Failed(named, $"this server has no tool '{named}'", NoSuchTool(context, named), "unknown_tool");

        var schema = tool.ProtocolTool.InputSchema;

        if (services.GetService<ArgumentCheck.Checked>() is not null && ArgumentCheck.Refusal(named, schema, step.Arguments?.Select(argument => argument.Key)) is { } wrong)
            return Step.Failed(named, wrong, ArgumentCheck.Takes(named, schema), ArgumentCheck.Type);

        var result = await ToolForwarding.CallAsync(context, tool, step.Arguments, cancellationToken).ConfigureAwait(false);

        Touch(context, tool);

        return Step.Of(named, result);
    }

    private static string NoSuchTool(RequestContext<CallToolRequestParams> context, string named)
    {
        var nearest = ArgumentCheck.Nearest(named, ServerTools.Of(context.Server).Select(tool => tool.ProtocolTool.Name));

        return nearest.Count > 0
            ? $"did you mean {string.Join(" or ", nearest.Select(name => $"'{name}'"))}? find_tool says what the server has"
            : "find_tool says what the server has";
    }

    /// <summary>A step of a loaded skill keeps the skill in use.</summary>
    private static void Touch(RequestContext<CallToolRequestParams> context, McpServerTool tool)
    {
        if (SessionRegistration.ServicesOf(context)?.GetService<SkillSessions>() is { } sessions && ToolCatalog.SkillOf(tool) is { } skill)
            sessions.Of(context.Server).Touch(skill, sessions.Time.GetUtcNow());
    }

    /// <summary>What one step answered: its data on success, why it failed otherwise.</summary>
    private sealed record Step(string Tool, bool IsOk, JsonNode? Data, string? Error, string? Hint, string? Type)
    {
        public static Step Failed(string tool, string error, string? hint, string? type) => new(tool, false, null, error, hint, type);

        /// <summary>The step's result read: the envelope's fields when it is one, the text as JSON or as words otherwise.</summary>
        public static Step Of(string tool, CallToolResult result)
        {
            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            var parsed = Parsed(text);
            var envelope = parsed is JsonObject { } fields && fields["ok"]?.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? fields : null;
            var isFailure = result.IsError == true || envelope?["ok"]?.GetValue<bool>() == false;

            if (isFailure)
                return Failed(tool, Words(envelope?["error"]) ?? text, Words(envelope?["hint"]), Words(envelope?["type"]));

            if (text.Length == 0 && result.Content.Count > 0)
                return new Step(tool, true, $"{result.Content.Count} block(s) that are not text; call the tool alone to see them", null, null, null);

            return new Step(tool, true, envelope is not null ? envelope["data"]?.DeepClone() : parsed, null, null, null);
        }

        private static string? Words(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node?.ToJsonString();

        private static JsonNode? Parsed(string text)
        {
            if (text.Length == 0)
                return null;

            try
            {
                return JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                return JsonValue.Create(text);
            }
        }
    }

    /// <summary>The steps run so far, as the answer carries them, inside the output budget.</summary>
    private sealed class Run(int budget)
    {
        private readonly JsonArray _steps = [];

        private int _left = budget;

        private int _omitted;

        public void Add(Step step)
        {
            var described = new JsonObject { ["tool"] = step.Tool, ["ok"] = step.IsOk };

            if (!step.IsOk)
            {
                described["error"] = step.Error;

                if (step.Hint is not null)
                    described["hint"] = step.Hint;

                _steps.Add((JsonNode)described);

                return;
            }

            var length = step.Data?.ToJsonString(ToolAnswer.Wire).Length ?? 0;

            if (length <= _left)
            {
                _left -= length;
                described["data"] = step.Data;
            }
            else
            {
                _omitted++;
                described["isOmitted"] = true;
            }

            _steps.Add((JsonNode)described);
        }

        public JsonObject Report()
        {
            var report = new JsonObject { ["ran"] = _steps.Count, ["steps"] = _steps.DeepClone() };

            if (_omitted > 0)
            {
                report["omitted"] = _omitted;
                report["note"] = $"{_omitted} answer(s) were left out to stay inside the output budget; call those tools on their own";
            }

            return report;
        }
    }
}

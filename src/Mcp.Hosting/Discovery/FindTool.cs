using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>The search behind <c>find_tool</c>: the catalog, scored against a task, as much of it as the session may call.</summary>
internal sealed class Discovery(DiscoveryOptions options)
{
    /// <summary>The tools one answer names unless the call asks for another number.</summary>
    public const int DefaultLimit = 5;

    /// <summary>The most tools one answer names, as the description of <c>limit</c> says.</summary>
    public const int MostTools = 20;

    /// <summary>The longest description a listing of every tool gives one, in characters.</summary>
    private const int LongestSummary = 160;

    private readonly Vocabulary _vocabulary = new(options.Noise, options.Prefix);

    /// <summary>Every tool the server offers, skills loaded or not, the parameters of a command named as the command takes them.</summary>
    public IReadOnlyList<ToolEntry> Catalog(McpServer server)
    {
        var catalog = options.Catalog ?? ToolCatalog.Of(ServerTools.Of(server), options.CommandOf);

        return options.ParameterOf is { } parameterOf ? [.. catalog.Select(entry => Commanded(entry, parameterOf))] : catalog;
    }

    /// <summary>An entry with a command, its parameters renamed as the command takes them and the ones it does not take dropped.</summary>
    private static ToolEntry Commanded(ToolEntry entry, Func<string, string, string?> parameterOf)
    {
        if (entry.Command is null)
            return entry;

        var named = entry.Parameters.Select(parameter => (Parameter: parameter, Name: parameterOf(entry.Name, parameter.Name)));

        return entry with { Parameters = [.. named.Where(each => each.Name is not null).Select(each => each.Parameter with { Name = each.Name! })] };
    }

    /// <summary>The entries that answer the task, best first and at most <paramref name="limit"/>; empty when none does.</summary>
    public IReadOnlyList<ToolEntry> Find(string? task, IReadOnlyList<ToolEntry> entries, int limit) =>
        ToolCatalog.Find(entries, task, Math.Clamp(limit, 1, MostTools), _vocabulary, options.Meanings);

    /// <summary>The entries as a listing of every tool shows them: the server's own, named by its prefix, first, the rest in their order after.</summary>
    /// <remarks>In the order of registration, <c>find_tool</c> and <c>run_tools</c> opened the list of Snail.MCP.CleanUp before any tool of its own.</remarks>
    public IReadOnlyList<ToolEntry> Listed(IReadOnlyList<ToolEntry> entries)
    {
        if (string.IsNullOrEmpty(options.Prefix))
            return entries;

        return [.. entries.OrderBy(entry => entry.Name.StartsWith(options.Prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1)];
    }

    /// <summary>The first sentence of a description, cut at a word when longer than a line.</summary>
    public static string Summary(string description)
    {
        var text = description.AsSpan().Trim();
        var lineEnd = text.IndexOfAny('\r', '\n');

        if (lineEnd >= 0)
            text = text[..lineEnd];

        var sentenceEnd = text.IndexOf(". ", StringComparison.Ordinal);

        if (sentenceEnd >= 0)
            text = text[..(sentenceEnd + 1)];

        if (text.Length <= LongestSummary)
            return text.ToString();

        var cut = text[..LongestSummary].LastIndexOf(' ');

        return $"{text[..(cut > 0 ? cut : LongestSummary)].TrimEnd(" ,;:")}…";
    }
}

/// <summary>The tool <c>find_tool</c>: which tools of the server answer a task, filtered by what the session may call.</summary>
/// <remarks>
/// <para>
/// The answer is the fleet's: <c>{task, isMatched, note, tools:[{name, command, description, parameters}]}</c>, and with
/// skills on, <c>skill</c> and <c>isLoaded</c> beside each tool. Nothing matched answers every tool by its name and the
/// first sentence of its description, so a model that asked in words the server does not use still sees what there is.
/// </para>
/// <para>
/// Only by name: an empty task answered with every tool's parameters weighed 70 357 characters in Snail.MCP.Roslyn, a
/// window's worth for a call that found nothing. The note says to ask again in words; the answer to that carries the
/// parameters.
/// </para>
/// <para>
/// A tool the session may not call (<see cref="IToolGate"/>) is not named: telling a model to call a tool the door will
/// refuse is a wrong answer that costs a round trip to find out (Snail.MCP.Git, Sandbox).
/// </para>
/// <para>
/// <c>intent</c> is taken for <c>task</c>: Snail.MCP.Blender's and AfterEffects' <c>find_tool</c> took <c>intent</c>, and
/// their logs show clients sending <c>task</c> to them; a client that learned either spelling is answered. So is one that
/// sends <c>query</c>, as Blender's logs show too — taken without being declared, since every session reads the schema.
/// </para>
/// </remarks>
internal static class FindTool
{
    public const string Name = "find_tool";

    /// <summary>The name taken for <c>task</c> beside <c>intent</c>, undeclared: the schema stays as short as every session reads it.</summary>
    private const string Query = "query";

    /// <summary>The tool as the server registers it: with <c>enable</c> where the server has skills, without where it has none.</summary>
    public static McpServerTool Create(IServiceProvider services)
    {
        var options = new McpServerToolCreateOptions { Services = services, SerializerOptions = DiscoveryWire.Tools };
        var examples = services.GetRequiredService<DiscoveryOptions>().Examples;
        var tool = services.GetService<SkillSessions>() is null
            ? McpServerTool.Create(Find, options)
            : McpServerTool.Create(FindAndEnableAsync, options);

        if (examples.Count > 0)
            tool.ProtocolTool.InputSchema = Described(tool.ProtocolTool.InputSchema, "task", ToolDescriptions.TaskWith(examples));

        return tool;
    }

    /// <summary>The name of the tool of Snail.Sdk.Mcp.Programs that runs a program of the server's commands.</summary>
    private const string RunProgram = "run_program";

    /// <summary>
    /// States the output schema of <c>find_tool</c> where the server states output schemas of its own, and with skills
    /// names the ways the server registered to run a skill's tools without loading it.
    /// </summary>
    /// <remarks>
    /// Written as "run_tools runs their tools without loading" whatever was registered, it pointed the model of a server
    /// whose own <c>run_tools</c> takes commands, or whose way is <c>run_program</c>, at the wrong tool (Snail.MCP.AfterEffects).
    /// <c>run_tools</c> is named when it is the SDK's (<c>WithToolSequences</c>), whose steps are tools; <c>run_program</c>
    /// whenever the server has it, since a program calls commands whatever registered it.
    /// </remarks>
    public static void Fit(McpServerOptions server, DiscoveryOptions options, IServiceProvider services)
    {
        if (server.ToolCollection is not { } tools || !tools.TryGetPrimitive(Name, out var find))
            return;

        if (OutputSchemas.AreStated(tools))
            find.ProtocolTool.OutputSchema = OutputSchemas.OfFindTool(options.Shape);

        if (services.GetService<SkillSessions>() is not null)
        {
            var runsTools = services.GetService<SequenceOptions>() is not null && tools.TryGetPrimitive(RunTools.Name, out _);

            find.ProtocolTool.Description = ToolDescriptions.FindToolWithSkillsAnd(runsTools, tools.TryGetPrimitive(RunProgram, out _));
        }
    }

    [McpServerTool(Name = Name, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FindTool)]
    [Accepts(Query)]
    private static CallToolResult Find(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.Task)] string? task = null,
        [Description(ToolDescriptions.Limit)] int limit = Discovery.DefaultLimit,
        [Description(ToolDescriptions.Intent)] string? intent = null) =>
        Answered(context, Answer(context, Asked(context, task, intent), limit, null, []));

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FindToolWithSkills)]
    [Accepts(Query)]
    private static async Task<CallToolResult> FindAndEnableAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.Task)] string? task = null,
        [Description(ToolDescriptions.Limit)] int limit = Discovery.DefaultLimit,
        [Description(ToolDescriptions.Enable)] bool enable = false,
        [Description(ToolDescriptions.Intent)] string? intent = null,
        CancellationToken cancellationToken = default)
    {
        var services = SessionRegistration.ServicesOf(context)!;
        var loaded = services.GetRequiredService<SkillSessions>().Of(context);
        var asked = Asked(context, task, intent);
        IReadOnlyList<string> enabled = [];

        if (enable)
            enabled = SkillTools.Load(context, loaded, Found(context, asked, limit).Select(entry => entry.Skill).OfType<string>());

        if (enabled.Count > 0)
            await SkillTools.ToldAsync(context.Server, cancellationToken).ConfigureAwait(false);

        return Answered(context, Answer(context, asked, limit, loaded, enabled));
    }

    private static CallToolResult Answered(RequestContext<CallToolRequestParams> context, JsonObject answer) =>
        ToolAnswer.Ok(answer, SessionRegistration.ServicesOf(context)!.GetRequiredService<DiscoveryOptions>().Shape);

    /// <summary>An input schema with the description of one property replaced.</summary>
    private static JsonElement Described(JsonElement schema, string property, string description)
    {
        var rewritten = JsonNode.Parse(schema.GetRawText())!;

        if (rewritten["properties"]?[property] is JsonObject described)
            described["description"] = description;

        return JsonElement.Parse(rewritten.ToJsonString());
    }

    /// <summary>The task, sent as <c>task</c>, <c>intent</c> or <c>query</c>, the first given.</summary>
    private static string? Asked(RequestContext<CallToolRequestParams> context, string? task, string? intent)
    {
        if (!string.IsNullOrWhiteSpace(task))
            return task;

        if (!string.IsNullOrWhiteSpace(intent))
            return intent;

        return context.Params?.Arguments?.TryGetValue(Query, out var query) == true && query.ValueKind == JsonValueKind.String ? query.GetString() : task ?? intent;
    }

    /// <summary>The tools the session may call that answer the task; empty when none does.</summary>
    private static IReadOnlyList<ToolEntry> Found(RequestContext<CallToolRequestParams> context, string? task, int limit)
    {
        var discovery = SessionRegistration.ServicesOf(context)!.GetRequiredService<Discovery>();

        return discovery.Find(task, Visible(context, discovery), limit);
    }

    /// <summary>The catalog without the tools the session may not call.</summary>
    private static IReadOnlyList<ToolEntry> Visible(RequestContext<CallToolRequestParams> context, Discovery discovery)
    {
        var services = SessionRegistration.ServicesOf(context);
        var catalog = discovery.Catalog(context.Server);

        if (services?.GetService<IToolGate>() is not { } gate)
            return catalog;

        return [.. catalog.Where(entry => gate.Refusal(entry.Name, services) is null)];
    }

    private static JsonObject Answer(RequestContext<CallToolRequestParams> context, string? task, int limit, LoadedSkills? loaded, IReadOnlyList<string> enabled)
    {
        var discovery = SessionRegistration.ServicesOf(context)!.GetRequiredService<Discovery>();
        var visible = Visible(context, discovery);
        var found = discovery.Find(task, visible, limit);
        var answer = new JsonObject { ["task"] = task, ["isMatched"] = found.Count > 0 };

        if (found.Count == 0)
        {
            answer["note"] = string.IsNullOrWhiteSpace(task)
                ? "no task was given; these are the server's tools by name: ask again with a task to see what each takes"
                : "nothing in this server's words matches; these are its tools by name: ask again in their words to see what each takes";
            answer["tools"] = new JsonArray([.. discovery.Listed(visible).Select(entry => Listed(entry, loaded))]);

            return answer;
        }

        answer["tools"] = new JsonArray([.. found.Select(entry => Described(entry, loaded))]);

        if (loaded is not null && enabled.Count > 0)
            answer["enabledSkills"] = new JsonArray([.. enabled.Select(skill => JsonValue.Create(skill))]);

        return answer;
    }

    private static JsonNode Described(ToolEntry entry, LoadedSkills? loaded)
    {
        var described = new JsonObject { ["name"] = entry.Name };

        if (entry.Command is not null)
            described["command"] = entry.Command;

        described["description"] = entry.Description;
        described["parameters"] = new JsonArray([.. entry.Parameters.Select(Described)]);

        return loaded is null ? described : Skilled(described, entry, loaded);
    }

    /// <summary>A tool as a listing of every tool names it: its name and the first sentence of its description, and its skill.</summary>
    private static JsonNode Listed(ToolEntry entry, LoadedSkills? loaded)
    {
        var listed = new JsonObject { ["name"] = entry.Name, ["description"] = Discovery.Summary(entry.Description) };

        return loaded is null ? listed : Skilled(listed, entry, loaded);
    }

    private static JsonObject Skilled(JsonObject described, ToolEntry entry, LoadedSkills loaded)
    {
        if (entry.Skill is not null)
            described["skill"] = entry.Skill;

        described["isLoaded"] = entry.Skill is null || loaded.IsLoaded(entry.Skill);

        return described;
    }

    private static JsonNode Described(ToolParameter parameter)
    {
        var described = new JsonObject { ["name"] = parameter.Name, ["type"] = parameter.Type, ["isRequired"] = parameter.IsRequired };

        if (parameter.Description is not null)
            described["description"] = parameter.Description;

        return described;
    }
}

/// <summary>The tools a server serves, from the collection the protocol looks a call up in.</summary>
internal static class ServerTools
{
    public static IReadOnlyList<McpServerTool> Of(McpServer server) =>
        server.ServerOptions.ToolCollection is { } tools ? [.. tools] : [];

    public static McpServerTool? Named(McpServer server, string? name) =>
        name is not null && server.ServerOptions.ToolCollection is { } tools && tools.TryGetPrimitive(name.Trim(), out var tool) ? tool : null;
}

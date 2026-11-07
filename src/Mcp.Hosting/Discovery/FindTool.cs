using System.ComponentModel;
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

    private readonly Vocabulary _vocabulary = new(options.Noise, options.Prefix);

    /// <summary>Every tool the server offers, skills loaded or not.</summary>
    public IReadOnlyList<ToolEntry> Catalog(McpServer server) => options.Catalog ?? ToolCatalog.Of(ServerTools.Of(server), options.CommandOf);

    /// <summary>The entries that answer the task, best first and at most <paramref name="limit"/>; empty when none does.</summary>
    public IReadOnlyList<ToolEntry> Find(string? task, IReadOnlyList<ToolEntry> entries, int limit)
    {
        var asked = _vocabulary.Asked(task);

        if (asked.Count == 0)
            return [];

        var answering = entries
            .Select(entry => (Entry: entry, Score: _vocabulary.Score(asked, entry.Name, options.Meanings.TryGetValue(entry.Name, out var meant) ? meant : null, entry.Description)))
            .Where(scored => scored.Score > 0);
        var best = answering
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Entry.Name, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, MostTools));

        return [.. best.Select(scored => scored.Entry)];
    }
}

/// <summary>The tool <c>find_tool</c>: which tools of the server answer a task, filtered by what the session may call.</summary>
/// <remarks>
/// <para>
/// The answer is the fleet's: <c>{task, isMatched, note, tools:[{name, command, description, parameters}]}</c>, and with
/// skills on, <c>skill</c> and <c>isLoaded</c> beside each tool. Nothing matched answers every tool, so a model that
/// asked in words the server does not use still sees what there is.
/// </para>
/// <para>
/// A tool the session may not call (<see cref="IToolGate"/>) is not named: telling a model to call a tool the door will
/// refuse is a wrong answer that costs a round trip to find out (Snail.MCP.Git, Sandbox).
/// </para>
/// <para>
/// <c>intent</c> is taken for <c>task</c>: Snail.MCP.Blender's and AfterEffects' <c>find_tool</c> took <c>intent</c>, and
/// their logs show clients sending <c>task</c> to them; a client that learned either spelling is answered.
/// </para>
/// </remarks>
internal static class FindTool
{
    public const string Name = "find_tool";

    /// <summary>The tool as the server registers it: with <c>enable</c> where the server has skills, without where it has none.</summary>
    public static McpServerTool Create(IServiceProvider services)
    {
        var options = new McpServerToolCreateOptions { Services = services, SerializerOptions = DiscoveryWire.Tools };

        return services.GetService<SkillSessions>() is null
            ? McpServerTool.Create(Find, options)
            : McpServerTool.Create(FindAndEnableAsync, options);
    }

    [McpServerTool(Name = Name, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FindTool)]
    private static CallToolResult Find(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.Task)] string? task = null,
        [Description(ToolDescriptions.Limit)] int limit = Discovery.DefaultLimit,
        [Description(ToolDescriptions.Intent)] string? intent = null) =>
        ToolAnswer.Ok(Answer(context, Asked(task, intent), limit, null, []));

    [McpServerTool(Name = Name, ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.FindToolWithSkills)]
    private static async Task<CallToolResult> FindAndEnableAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.Task)] string? task = null,
        [Description(ToolDescriptions.Limit)] int limit = Discovery.DefaultLimit,
        [Description(ToolDescriptions.Enable)] bool enable = false,
        [Description(ToolDescriptions.Intent)] string? intent = null,
        CancellationToken cancellationToken = default)
    {
        var services = SessionRegistration.ServicesOf(context)!;
        var loaded = services.GetRequiredService<SkillSessions>().Of(context.Server);
        var asked = Asked(task, intent);
        IReadOnlyList<string> enabled = [];

        if (enable)
            enabled = SkillTools.Load(context, loaded, Found(context, asked, limit).Select(entry => entry.Skill).OfType<string>());

        if (enabled.Count > 0)
            await SkillTools.ToldAsync(context.Server, cancellationToken).ConfigureAwait(false);

        return ToolAnswer.Ok(Answer(context, asked, limit, loaded, enabled));
    }

    private static string? Asked(string? task, string? intent) => string.IsNullOrWhiteSpace(task) ? intent : task;

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
            answer["note"] = string.IsNullOrWhiteSpace(task) ? "no task was given; these are all of the server's tools" : "nothing in this server's words matches; these are all of its tools";

        answer["tools"] = new JsonArray([.. (found.Count > 0 ? found : visible).Select(entry => Described(entry, loaded))]);

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

        if (loaded is null)
            return described;

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

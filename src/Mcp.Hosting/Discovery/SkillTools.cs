using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>The tools that load and unload skills — <c>&lt;short&gt;_skills</c>, <c>_enable_skill</c>, <c>_disable_skill</c> — and the filters that make a session's list its skills'.</summary>
internal static class SkillTools
{
    /// <summary>The three tools, named by the server's short name.</summary>
    public static IEnumerable<Func<IServiceProvider, McpServerTool>> All(string name) =>
    [
        services => McpServerTool.Create(ListSkills, Options(services, $"{name}_skills", ToolDescriptions.Skills, isReadOnly: true)),
        services => McpServerTool.Create(EnableAsync, Options(services, $"{name}_enable_skill", ToolDescriptions.EnableSkill, isReadOnly: false)),
        services => McpServerTool.Create(DisableAsync, Options(services, $"{name}_disable_skill", ToolDescriptions.DisableSkill, isReadOnly: false)),
    ];

    /// <summary>Loads the named skills in the session, as far as the gate lets it call their tools; the ones it loaded.</summary>
    public static IReadOnlyList<string> Load(RequestContext<CallToolRequestParams> context, LoadedSkills loaded, IEnumerable<string> skills)
    {
        var services = SessionRegistration.ServicesOf(context)!;
        var now = services.GetRequiredService<SkillSessions>().Time.GetUtcNow();
        var declared = Callable(context);
        var wanted = skills.Distinct(StringComparer.OrdinalIgnoreCase).Select(skill => Named(declared, skill)).OfType<Skill>();

        return [.. wanted.Where(skill => loaded.TryLoad(skill.Name, now)).Select(skill => skill.Name)];
    }

    /// <summary>The revision from which a client hears only the notices it subscribed to.</summary>
    private const string SubscribingRevision = "2026-07-28";

    /// <summary>Tells the session its tool list changed: in the reply to the call that changed it, or through its subscription.</summary>
    /// <remarks>
    /// A client before the 2026-07-28 revision is told in the response it is already reading: over HTTP Claude Code never
    /// saw a skill's tools arrive until Snail.MCP.Blender sent the notice there. A client of that revision or later must hear
    /// only what it subscribed to, so the collection announces the change and the listen streams that asked for
    /// <c>toolsListChanged</c> carry it (<see cref="Jobs.ListenStreams"/>) — a sessionless client's included, which has
    /// no other channel.
    /// </remarks>
    public static async Task ToldAsync(McpServer server, CancellationToken cancellationToken)
    {
        var revision = server.NegotiatedProtocolVersion;

        if (revision is not null && string.CompareOrdinal(revision, SubscribingRevision) < 0)
        {
            await server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken).ConfigureAwait(false);

            return;
        }

        (server.ServerOptions.ToolCollection as SkillToolCollection)?.Announce();
    }

    /// <summary>The list without the tools of the skills the session has not loaded.</summary>
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Hiding(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, cancellationToken) =>
        {
            var listed = await next(context, cancellationToken).ConfigureAwait(false);

            if (SessionRegistration.ServicesOf(context)?.GetService<SkillSessions>() is not { } sessions)
                return listed;

            var loaded = sessions.Of(context.Server);

            listed.Tools = [.. listed.Tools.Where(tool => SkillOf(context.Server, tool.Name) is not { } skill || loaded.IsLoaded(skill))];

            return listed;
        };

    /// <summary>A call to a tool of a skill the session has not loaded loads it first; any call marks its skill used.</summary>
    /// <remarks>
    /// The client that names a tool it was not listed learned it somewhere — from <c>find_tool</c>, or a list it read
    /// before the skill expired — and loading the skill is no permission: what the session may call is the gate's.
    /// </remarks>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Loading(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            var services = SessionRegistration.ServicesOf(context);

            if (services?.GetService<SkillSessions>() is not { } sessions || context.MatchedPrimitive is not McpServerTool tool || ToolCatalog.SkillOf(tool) is not { } skill)
                return await next(context, cancellationToken).ConfigureAwait(false);

            var loaded = sessions.Of(context.Server);
            var isAllowed = services.GetService<IToolGate>()?.Refusal(tool.ProtocolTool.Name, services) is null;

            if (isAllowed && loaded.TryLoad(skill, sessions.Time.GetUtcNow()))
                await ToldAsync(context.Server, cancellationToken).ConfigureAwait(false);

            loaded.Touch(skill, sessions.Time.GetUtcNow());

            return await next(context, cancellationToken).ConfigureAwait(false);
        };

    private static CallToolResult ListSkills(RequestContext<CallToolRequestParams> context)
    {
        var loaded = SessionsOf(context).Of(context.Server);
        var skills = Callable(context);

        return ToolAnswer.Ok(new JsonObject { ["skills"] = new JsonArray([.. skills.Select(skill => Described(skill, loaded))]) });
    }

    private static async Task<CallToolResult> EnableAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.SkillName)] string name,
        CancellationToken cancellationToken = default)
    {
        var sessions = SessionsOf(context);
        var skills = Callable(context);

        if (Named(skills, name) is not { } skill)
            return Unknown(name, skills);

        var loaded = sessions.Of(context.Server);

        if (loaded.TryLoad(skill.Name, sessions.Time.GetUtcNow()))
            await ToldAsync(context.Server, cancellationToken).ConfigureAwait(false);

        return ToolAnswer.Ok(Described(skill, loaded));
    }

    private static async Task<CallToolResult> DisableAsync(
        RequestContext<CallToolRequestParams> context,
        [Description(ToolDescriptions.SkillName)] string name,
        CancellationToken cancellationToken = default)
    {
        var skills = Callable(context);

        if (Named(skills, name) is not { } skill)
            return Unknown(name, skills);

        var loaded = SessionsOf(context).Of(context.Server);

        if (loaded.TryUnload(skill.Name))
            await ToldAsync(context.Server, cancellationToken).ConfigureAwait(false);

        return ToolAnswer.Ok(Described(skill, loaded));
    }

    private static McpServerToolCreateOptions Options(IServiceProvider services, string name, string description, bool isReadOnly) => new()
    {
        Services = services,
        SerializerOptions = DiscoveryWire.Tools,
        Name = name,
        Description = description,
        ReadOnly = isReadOnly,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
    };

    private static SkillSessions SessionsOf(RequestContext<CallToolRequestParams> context) =>
        SessionRegistration.ServicesOf(context)!.GetRequiredService<SkillSessions>();

    /// <summary>The skills of the server with only the tools the session may call; a skill left with none is not offered.</summary>
    private static IReadOnlyList<Skill> Callable(RequestContext<CallToolRequestParams> context)
    {
        var services = SessionRegistration.ServicesOf(context);
        var declared = Skill.Of(ServerTools.Of(context.Server));

        if (services?.GetService<IToolGate>() is not { } gate)
            return declared;

        var narrowed = declared.Select(skill => skill with { Tools = [.. skill.Tools.Where(tool => gate.Refusal(tool, services) is null)] });

        return [.. narrowed.Where(skill => skill.Tools.Count > 0)];
    }

    private static Skill? Named(IReadOnlyList<Skill> skills, string? name) =>
        skills.FirstOrDefault(skill => string.Equals(skill.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? SkillOf(McpServer server, string tool) =>
        ServerTools.Named(server, tool) is { } found ? ToolCatalog.SkillOf(found) : null;

    private static CallToolResult Unknown(string name, IReadOnlyList<Skill> skills) =>
        ToolAnswer.Failed($"this server has no skill '{name}'", $"its skills are {string.Join(", ", skills.Select(skill => skill.Name))}", "unknown_skill");

    private static JsonObject Described(Skill skill, LoadedSkills loaded) => new()
    {
        ["name"] = skill.Name,
        ["description"] = skill.Description,
        ["isLoaded"] = loaded.IsLoaded(skill.Name),
        ["tools"] = new JsonArray([.. skill.Tools.Select(tool => JsonValue.Create(tool))]),
    };
}

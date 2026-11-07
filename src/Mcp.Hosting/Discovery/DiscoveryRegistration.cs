using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>The ways in that every server of the fleet spells alike: <c>find_tool</c>, <c>run_tools</c>, the argument check, and skills.</summary>
/// <remarks>
/// Each was written in the servers themselves — <c>find_tool</c> in seven, <c>run_tools</c> in five, skills in two — and
/// the copies parted: one answered <c>intent</c> and another <c>task</c>, one checked the grant in its steps and two did
/// not. Here each is written once, on the tools the server registers, so a server adopts it with one line and deletes
/// its own.
/// </remarks>
public static class DiscoveryRegistration
{
    extension(IMcpServerBuilder builder)
    {
        /// <summary>Adds <c>find_tool(task, limit)</c>: the tools that answer a task in words, best first, each with what it takes.</summary>
        /// <remarks>
        /// <para>
        /// The catalog is every tool the server registered — the skills' included, loaded or not — read from their input
        /// schemas when the tool is called, or <see cref="DiscoveryOptions.Catalog"/>. A tool the session may not call is
        /// left out (<see cref="Sessions.IToolGate"/>). <c>intent</c> is taken for <c>task</c>. With
        /// <see cref="WithSkills"/>, each tool carries its <c>skill</c> and <c>isLoaded</c>, and <c>enable: true</c> loads the
        /// skills of the tools found. The answer is <see cref="Answers.ToolAnswer.Ok"/> of
        /// <c>{task, isMatched, note, tools:[{name, command, description, parameters:[{name, type, isRequired, description}]}]}</c>.
        /// </para>
        /// <para>Called twice, it registers once.</para>
        /// </remarks>
        public IMcpServerBuilder WithToolDiscovery(Action<DiscoveryOptions>? configure = null)
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(DiscoveryOptions)))
                return builder;

            var options = new DiscoveryOptions();
            configure?.Invoke(options);

            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(new Discovery(options));
            builder.Services.AddSingleton<McpServerTool>(FindTool.Create);

            return builder;
        }

        /// <summary>Adds <c>run_tools(steps)</c>: several tools of the server in one call, in order, stopping at the first that fails.</summary>
        /// <remarks>
        /// <para>
        /// A step is <c>{"tool": "…", "arguments": {…}}</c>, run through <see cref="Sessions.ToolForwarding.CallAsync"/>, so
        /// the session's grant holds for every step and no forwarder is a step. The answer is the envelope with each step's
        /// <c>{tool, ok, data}</c>, or the failure of the step that stopped the run with the steps before it as its data;
        /// an answer past <see cref="SequenceOptions.OutputBudget"/> is left out and flagged <c>isOmitted</c>. With
        /// <see cref="WithArgumentCheck"/>, each step's arguments are checked as a direct call's are.
        /// </para>
        /// <para>Called twice, it registers once.</para>
        /// </remarks>
        public IMcpServerBuilder WithToolSequences(Action<SequenceOptions>? configure = null)
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(SequenceOptions)))
                return builder;

            var options = new SequenceOptions();
            configure?.Invoke(options);

            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<McpServerTool>(RunTools.Create);

            return builder;
        }

        /// <summary>
        /// Refuses a call that leaves out a parameter its tool requires or sends one it does not take, naming it and the
        /// parameter it probably meant, before the tool runs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Without it, a missing parameter reaches the client as the MCP SDK's "An error occurred invoking …", and a
        /// parameter the tool does not take is dropped without a word. The refusal is <see cref="Answers.ToolAnswer.Failed"/>
        /// with <c>type</c> <c>arguments</c> and what the tool takes as the hint; a guess is offered within a third of the
        /// name's length in edits, or when the name is a short form of a parameter (<c>timeout</c> for
        /// <c>timeoutSeconds</c>), and none for a word like nothing the tool takes.
        /// </para>
        /// <para>
        /// A filter in the order of registration: called after <c>RefuseWhatWasNotGranted</c>, a session learns nothing about
        /// a tool it may not call. Called twice, it registers once.
        /// </para>
        /// </remarks>
        public IMcpServerBuilder WithArgumentCheck()
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(ArgumentCheck.Checked)))
                return builder;

            builder.Services.AddSingleton(new ArgumentCheck.Checked());
            builder.WithRequestFilters(filters => filters.AddCallToolFilter(ArgumentCheck.Checking));

            return builder;
        }

        /// <summary>
        /// Loads skills — the tools of the classes marked <see cref="SkillAttribute"/> — per session on demand: their tools are
        /// not listed until the session loads the skill, and <c>&lt;short&gt;_skills</c>, <c>_enable_skill</c> and
        /// <c>_disable_skill</c> list, load and unload them.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The skills' classes are registered as any other (<c>.WithTools&lt;MaterialTools&gt;()</c>): every tool stays in the
        /// server's collection, so the protocol finds every call and <c>find_tool</c> every tool, and a session's list leaves
        /// out what it has not loaded. Loading or unloading tells the session with <c>notifications/tools/list_changed</c>
        /// in the reply to the call. A call to a tool of a skill not loaded loads it; a step of <c>run_tools</c> does not.
        /// Over stdio and for a sessionless client there is one set of loaded skills; each MCP session has its own.
        /// </para>
        /// <para>
        /// The tools are named by <see cref="SkillOptions.Name"/>, the server's short name. A skill unused for
        /// <see cref="SkillOptions.IdleTimeout"/> is unloaded, and every session told its list changed — the MCP SDK's own
        /// notice of a changed collection, which reaches a session outside any request. Called twice, it registers once.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">No short name is set and none is written into the entry assembly.</exception>
        public IMcpServerBuilder WithSkills(Action<SkillOptions>? configure = null)
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(SkillOptions)))
                return builder;

            var options = new SkillOptions();
            configure?.Invoke(options);

            var name = options.Name ?? ServerSettings.NameOf(Assembly.GetEntryAssembly())
                ?? throw new InvalidOperationException("skills are named by the server's short name, and none was found: set SkillOptions.Name");

            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(services => new SkillSessions(name, services.GetRequiredService<TimeProvider>()));
            builder.Services.AddHostedService<SkillExpiry>();

            foreach (var tool in SkillTools.All(name))
                builder.Services.AddSingleton<McpServerTool>(tool);

            builder.Services.Configure<McpServerOptions>(Announcing);
            builder.Services.PostConfigure<McpServerOptions>(Collecting);
            builder.WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter(SkillTools.Hiding);
                filters.AddCallToolFilter(SkillTools.Loading);
            });

            return builder;
        }
    }

    /// <summary>The server's tools in a collection an expiry can announce through, once every other setup has filled it.</summary>
    private static void Collecting(McpServerOptions options) =>
        options.ToolCollection = new SkillToolCollection(options.ToolCollection ?? []);

    /// <summary>Says the server tells of a change to its tool list, which loading a skill is.</summary>
    private static void Announcing(McpServerOptions options)
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Tools ??= new ToolsCapability();
        options.Capabilities.Tools.ListChanged = true;
    }
}

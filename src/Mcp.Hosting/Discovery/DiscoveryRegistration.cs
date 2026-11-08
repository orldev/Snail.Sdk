using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Jobs;

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
        /// left out (<see cref="Sessions.IToolGate"/>). <c>intent</c> and <c>query</c> are taken for <c>task</c>. With
        /// <see cref="WithSkills"/>, each tool carries its <c>skill</c> and <c>isLoaded</c>, <c>enable: true</c> loads the
        /// skills of the tools found, and the description names the ways the server registered to run their tools without
        /// loading them — <c>run_tools</c> of <see cref="WithToolSequences"/>, <c>run_program</c>, both or neither. The answer
        /// is <see cref="Answers.ToolAnswer.Ok(System.Text.Json.Nodes.JsonNode, Answers.AnswerShape)"/>
        /// of <c>{task, isMatched, note, tools:[{name, command, description, parameters:[{name, type, isRequired, description}]}]}</c>
        /// in <see cref="DiscoveryOptions.Shape"/>, the parameters of a tool with a command named as the command takes them
        /// (<see cref="DiscoveryOptions.ParameterOf"/>); when nothing matches, every tool by its name and the first sentence
        /// of its description, those of <see cref="DiscoveryOptions.Prefix"/> first. Once another tool of the server states
        /// an output schema, <c>find_tool</c> states its own.
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
            builder.Services.AddOptions<McpServerOptions>().PostConfigure<IServiceProvider>((server, services) => FindTool.Fit(server, options, services));

            return builder;
        }

        /// <summary>Adds <c>run_tools(steps)</c>: several tools of the server in one call, in order, stopping at the first that fails.</summary>
        /// <remarks>
        /// <para>
        /// A step is <c>{"tool": "…", "arguments": {…}}</c>, run through <see cref="Sessions.ToolForwarding.CallAsync"/>, so
        /// the session's grant holds for every step and no forwarder is a step. The answer is the envelope with each step's
        /// <c>{tool, ok, data}</c>, or the failure of the step that stopped the run with the steps before it as its data;
        /// an answer past <see cref="SequenceOptions.OutputBudget"/> is left out and flagged <c>isOmitted</c>. A step that
        /// throws fails as a direct call answered by <c>ExplainFailures</c> would. With <see cref="WithArgumentCheck"/>,
        /// each step's arguments are checked as a direct call's are.
        /// </para>
        /// <para>
        /// The hints of <c>run_tools</c> are those of the tools it can run — read-only, destructive, open-world — read once
        /// the server's tools are collected, and its output schema is stated once another tool states one.
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
            builder.Services.PostConfigure<McpServerOptions>(server => RunTools.Fit(server, options));

            return builder;
        }

        /// <summary>
        /// Refuses a call that leaves out a parameter its tool requires or sends one it does not take, naming it and the
        /// parameter it probably meant, before the tool runs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Without it, a missing parameter reaches the client as the MCP SDK's "An error occurred invoking …", and a
        /// parameter the tool does not take is dropped without a word. The refusal is <see cref="Answers.ToolAnswer.Failed(string, string, string, System.Text.Json.Nodes.JsonNode)"/>
        /// with <c>type</c> <c>arguments</c> and what the tool takes as the hint; a guess is offered within a third of the
        /// name's length in edits, or when the name is a short form of a parameter (<c>timeout</c> for
        /// <c>timeoutSeconds</c>), and none for a word like nothing the tool takes.
        /// </para>
        /// <para>
        /// A name spelled like nothing it means is offered what <see cref="ArgumentCheckOptions.Synonyms"/> says it stands
        /// for: <c>.WithArgumentCheck(options => options.Synonyms["query"] = ["task", "intent"])</c>.
        /// </para>
        /// <para>
        /// A filter in the order of registration: called after <c>RefuseWhatWasNotGranted</c>, a session learns nothing about
        /// a tool it may not call. Called twice, it registers once.
        /// </para>
        /// </remarks>
        public IMcpServerBuilder WithArgumentCheck(Action<ArgumentCheckOptions>? configure = null)
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(ArgumentCheck.Checked)))
                return builder;

            var options = new ArgumentCheckOptions();
            configure?.Invoke(options);

            builder.Services.AddSingleton(new ArgumentCheck.Checked(options));
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
        /// in the reply to the call before the 2026-07-28 revision, and from it on the client's <c>subscriptions/listen</c>
        /// stream when it asked for <c>toolsListChanged</c> — a sessionless client over HTTP among them, whose listen stream
        /// this puts in place of the MCP SDK's, which grants such a client nothing. A call to a tool of a skill not loaded
        /// loads it; a step of <c>run_tools</c> does not.
        /// Each MCP session has its own set of loaded skills; over stdio and for sessionless clients there is one, unless
        /// <see cref="SkillOptions.SessionOf"/> tells them apart.
        /// </para>
        /// <para>
        /// The tools are named by <see cref="SkillOptions.Prefix"/>, the server's short name. A skill unused for
        /// <see cref="SkillOptions.IdleTimeout"/> is unloaded unless <see cref="SkillOptions.IsBusy"/> says it is at work,
        /// and every session told its list changed — the MCP SDK's own notice of a changed collection, which reaches a
        /// session outside any request. Called twice, it registers once.
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">No short name is set and none is written into the entry assembly.</exception>
        public IMcpServerBuilder WithSkills(Action<SkillOptions>? configure = null)
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(SkillOptions)))
                return builder;

            var options = new SkillOptions();
            configure?.Invoke(options);

            var prefix = (options.Prefix ?? ServerSettings.NameOf(Assembly.GetEntryAssembly()))?.TrimEnd('_') is { Length: > 0 } named
                ? named
                : throw new InvalidOperationException("skills are named by the server's short name, and none was found: set SkillOptions.Prefix");

            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(services => new SkillSessions(prefix, services.GetRequiredService<TimeProvider>(), options.SessionOf));
            builder.Services.AddSingleton<SharedToolCollection>();
            builder.Services.AddHostedService<SkillExpiry>();
            ListenStreams.Register(builder.Services);

            foreach (var tool in SkillTools.All(prefix))
                builder.Services.AddSingleton<McpServerTool>(tool);

            builder.Services.Configure<McpServerOptions>(Announcing);
            builder.Services.AddOptions<McpServerOptions>().PostConfigure<SharedToolCollection>((server, shared) => server.ToolCollection = shared.Of(server.ToolCollection));
            builder.WithRequestFilters(filters =>
            {
                filters.AddListToolsFilter(SkillTools.Hiding);
                filters.AddCallToolFilter(SkillTools.Loading);
            });

            return builder;
        }
    }

    /// <summary>Says the server tells of a change to its tool list, which loading a skill is.</summary>
    private static void Announcing(McpServerOptions options)
    {
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Tools ??= new ToolsCapability();
        options.Capabilities.Tools.ListChanged = true;
    }
}

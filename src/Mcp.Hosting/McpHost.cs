using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The host a stdio MCP server runs in.</summary>
/// <remarks>
/// The empty one: the full builder's appsettings, their file watchers, the command line and the default loggers are not
/// loaded; the server's own settings are, its settings file and its variables (<see cref="ServerSettings"/>), and the log
/// levels they name (<c>UseLevelsFrom</c>). stdout carries JSON-RPC, so log lines go to stderr. The heap is
/// compacted once the server has gone quiet (<c>AddIdleTrim</c>); a server that wants its orphaned process ended adds
/// <c>AddIdleShutdown</c>.
/// </remarks>
public static class McpHost
{
    /// <summary>An empty host builder that logs to stderr at the levels its settings name and trims the heap when the server is idle.</summary>
    /// <remarks>
    /// <para>
    /// For a program built on Snail.Sdk.Mcp, which names its server in its assembly, the server's settings are read — and,
    /// first, its folders moved from the snail names (<see cref="ServerFolders.MoveFromSnailNames(string)"/>), so a
    /// settings file kept under the old name is found under the new one by this very start.
    /// </para>
    /// <para>
    /// The server is found by the entry assembly, the program the process started. A host built in a test's own process
    /// has the test runner (testhost) for its entry assembly, which names no server, so it reads no settings at all; such
    /// a test, and a program that has read its settings already, passes them to <see cref="CreateBuilder(string[], IConfiguration)"/>.
    /// </para>
    /// </remarks>
    public static HostApplicationBuilder CreateBuilder(string[] args) => CreateBuilder(args, Assembly.GetEntryAssembly());

    /// <summary>An empty host builder over the settings the program has read already, logging to stderr at the levels they name and trimming the heap when the server is idle.</summary>
    /// <param name="args">The program's arguments.</param>
    /// <param name="settings">The server's settings as the program holds them — <see cref="ServerSettings.Read(string)"/>, with whatever the server adds to them — which become the host's configuration.</param>
    /// <remarks>
    /// <para>
    /// The same host as <see cref="CreateBuilder(string[])"/>, but the settings are not read a second time: a program reads
    /// them before any host exists, to choose its transport (<see cref="McpEndpoint.Read(IConfiguration, Uri)"/>), and
    /// a server whose settings carry more than <see cref="ServerSettings"/> reads — the browser defaults of
    /// Snail.MCP.Playwright — added its own configuration again on top of the one the host had just read. Here the
    /// host's configuration is <paramref name="settings"/>, read once, and nothing is looked for by the entry assembly,
    /// so a host built in a test's process has the settings the test gives it.
    /// </para>
    /// <para>
    /// The folders are not moved from the snail names here: reading the settings moved them
    /// (<see cref="ServerSettings.Read(string)"/>, <c>AddServerSettings(name)</c>).
    /// </para>
    /// </remarks>
    public static HostApplicationBuilder CreateBuilder(string[] args, IConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

        builder.Configuration.AddConfiguration(settings);

        return Finished(builder);
    }

    /// <summary>The host builder of the server built into <paramref name="server"/>.</summary>
    internal static HostApplicationBuilder CreateBuilder(string[] args, Assembly? server)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

        if (ServerSettings.NameOf(server) is { } name)
            builder.Configuration.AddServerSettings(name);

        return Finished(builder);
    }

    /// <summary>The builder logging to stderr at the levels its configuration names, the heap trimmed when idle.</summary>
    private static HostApplicationBuilder Finished(HostApplicationBuilder builder)
    {
        builder.Logging.UseStderr().UseLevelsFrom(builder.Configuration);
        builder.Services.AddIdleTrim();

        return builder;
    }

    extension(ILoggingBuilder logging)
    {
        /// <summary>Clears every logger and writes a line to stderr from information up, or from the level a filter sets.</summary>
        public ILoggingBuilder UseStderr()
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(new StderrLogging());

            return logging;
        }

        /// <summary>Applies the log levels <paramref name="settings"/> name, over defaults that keep the MCP SDK and ASP.NET Core to warnings and up.</summary>
        /// <param name="settings">The server's settings — the host's configuration, which both hosts fill with them.</param>
        /// <remarks>
        /// <para>
        /// The levels are the <c>Logging</c> section of .NET, <c>Logging:LogLevel:&lt;category&gt;</c> and
        /// <c>Logging:LogLevel:Default</c>, read from three places, each over the one before: the plain
        /// <c>Logging__LogLevel__…</c> variables that .NET reads everywhere and an image's <c>Dockerfile</c> sets as its
        /// defaults, then the server's settings file, then its <c>SNAIL_MCP_&lt;NAME&gt;_LOGGING__LOGLEVEL__…</c> variables —
        /// a category with a dot in it written with the dot, <c>…_LOGLEVEL__MICROSOFT.ASPNETCORE</c>, where the shell allows it.
        /// Both hosts are empty builders, which bound no <c>Logging</c> section, so the levels an image set were silently
        /// ignored (Snail.MCP.Documents, 2026-11-07).
        /// </para>
        /// <para>
        /// Without a level of their own, <c>ModelContextProtocol</c> and <c>Microsoft.AspNetCore</c> write from warnings up:
        /// the MCP SDK writes three lines at information for every call, which over stdio land in the client's log of the
        /// server and over HTTP in the job's. A category the settings name takes their level instead, the default included.
        /// </para>
        /// </remarks>
        public ILoggingBuilder UseLevelsFrom(IConfiguration settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            logging.AddFilter(ModelContextProtocolCategory, LogLevel.Warning);
            logging.AddFilter(AspNetCoreCategory, LogLevel.Warning);
            logging.AddConfiguration(LevelsOf(settings, Environment.GetEnvironmentVariables()));

            return logging;
        }
    }

    extension(IServiceCollection services)
    {
        /// <summary>Compacts the heap once the server has heard no MCP message for five minutes; both hosts call it already.</summary>
        /// <remarks>
        /// <para>
        /// <c>SNAIL_MCP_IDLE_TRIM</c>, read once when the host starts, sets the quiet period in seconds, and 0 turns the
        /// trim off. It is one variable for every server rather than a <c>SNAIL_MCP_&lt;NAME&gt;_</c> setting of each: the
        /// trim belongs to this host, as a server's own settings belong to the server.
        /// </para>
        /// <para>
        /// The messages are counted by an incoming-message filter on <see cref="McpServerOptions"/>, so stdio and every
        /// HTTP session alike keep the server awake. Called twice, it registers once.
        /// </para>
        /// </remarks>
        public IServiceCollection AddIdleTrim()
        {
            if (services.Any(service => service.ServiceType == typeof(IdleTrimRegistered)))
                return services;

            AddActivity(services);
            services.AddSingleton(new IdleTrimRegistered());
            services.AddHostedService(provider => new IdleTrim(
                provider.GetRequiredService<McpActivity>(),
                IdleTrim.PeriodOf(Environment.GetEnvironmentVariable(IdleTrim.Variable)),
                TimeOf(provider),
                () => GC.GetTotalAllocatedBytes(),
                IdleTrim.Compact,
                provider.GetRequiredService<ILogger<IdleTrim>>()));

            return services;
        }

        /// <summary>Lets the thread pool grow at once to <paramref name="workers"/> worker threads when the host starts, rather than one or two a second.</summary>
        /// <param name="workers">The worker threads the pool makes without waiting once there is work for them.</param>
        /// <remarks>
        /// <para>
        /// The pool starts with as many workers as the machine has cores and adds the rest slowly, about one or two a
        /// second. A server that does much blocking work at once — Snail.MCP.CleanUp's census runs 27 kinds and trails side
        /// by side, many of them walking the disk without awaiting anything — holds every worker, and a tool call that came
        /// in meanwhile waited for a thread to answer on: 7 to 8 seconds, measured on 2026-11-01, until the server raised
        /// the minimum to 64, twice what a census runs at once. A minimum is not a reservation: a thread is made only when
        /// there is work for it, and an idle server keeps none.
        /// </para>
        /// <para>
        /// The minimum is raised as the host starts, before any hosted service starts its work, and never lowered: a pool
        /// that already allows more keeps its number, and so does the completion-port minimum. Called again, the largest
        /// number asked for holds. It works the same in either host.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The number is zero or negative.</exception>
        public IServiceCollection AddReadyThreads(int workers)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workers);

            var asked = services.FirstOrDefault(service => service.ServiceType == typeof(ReadyThreads))?.ImplementationInstance as ReadyThreads;

            if (asked is not null && asked.Workers >= workers)
                return services;

            services.Replace(ServiceDescriptor.Singleton(new ReadyThreads(workers)));

            if (asked is null)
                services.AddHostedService(provider => provider.GetRequiredService<ReadyThreads>());

            return services;
        }

        /// <summary>Stops a stdio server that has heard no MCP message for <paramref name="after"/>, unless <paramref name="isBusy"/> says it is busy.</summary>
        /// <param name="after">The quiet period; <see cref="TimeSpan.Zero"/> or less turns the shutdown off.</param>
        /// <param name="isBusy">Whether the server is at work no MCP message shows — a plugin's bridge connected, a render running; while it says yes, the server counts as heard.</param>
        /// <remarks>
        /// <para>
        /// For the stdio server a client started and then lost without closing its stdin: the process would otherwise
        /// wait for a message that never comes. It is not <see cref="AddIdleTrim"/>, which compacts the heap of a server
        /// that stays. Any MCP message counts — a call, a listing, a ping, a notification — over the same filter the trim
        /// counts with; the watchers the servers kept of their own counted <c>tools/call</c> alone, so a client that only
        /// pings now keeps the server up, as a client that is alive should.
        /// </para>
        /// <para>
        /// A server served over HTTP is never stopped, whatever the period: it serves every session of the machine, and
        /// is kept running by launchd or its container. It is told apart by its transport — only a server with one
        /// session (<c>WithStdioServerTransport</c>, <c>WithStreamServerTransport</c>) is watched — so a program of both
        /// transports registers this alike in either.
        /// </para>
        /// <para>
        /// The period is the server's to choose, and its setting's: the host reads no variable for it. A period of zero or
        /// less turns the shutdown off rather than failing the start: the servers pass their setting straight through, and a
        /// negative one stopped the server at its start with an exception that named no setting (Snail.MCP.Figma, 0.5.0).
        /// Called again, the later period and veto replace the earlier ones.
        /// </para>
        /// <para>
        /// The quiet is measured by the <see cref="TimeProvider"/> the services hold, <see cref="TimeProvider.System"/> when
        /// they hold none, so a test that registers a clock of its own moves the period with it. The stop is logged at
        /// warning: the process ends without a client asking it to, and the watchers of the fleet wrote it at warning.
        /// </para>
        /// </remarks>
        public IServiceCollection AddIdleShutdown(TimeSpan after, Func<IServiceProvider, bool>? isBusy = null)
        {
            var isRegistered = services.Any(service => service.ServiceType == typeof(IdleShutdownPlan));

            AddActivity(services);
            services.Replace(ServiceDescriptor.Singleton(new IdleShutdownPlan(after, isBusy)));

            if (isRegistered)
                return services;

            services.AddHostedService(provider =>
            {
                var plan = provider.GetRequiredService<IdleShutdownPlan>();
                var isSingleSession = provider.GetService<IServiceProviderIsService>()?.IsService(typeof(ITransport)) ?? false;

                return new IdleShutdown(
                    provider.GetRequiredService<McpActivity>(),
                    plan.After,
                    plan.IsBusy is { } veto ? () => veto(provider) : static () => false,
                    isSingleSession,
                    TimeOf(provider),
                    provider.GetRequiredService<IHostApplicationLifetime>(),
                    provider.GetRequiredService<ILogger<IdleShutdown>>());
            });

            return services;
        }
    }

    /// <summary>The category of every logger of the MCP SDK.</summary>
    internal const string ModelContextProtocolCategory = "ModelContextProtocol";

    private const string AspNetCoreCategory = "Microsoft.AspNetCore";

    private const string LoggingVariablePrefix = "Logging__";

    /// <summary>The <c>Logging</c> section: the plain <c>Logging__</c> variables among <paramref name="variables"/>, under <paramref name="settings"/>.</summary>
    internal static IConfiguration LevelsOf(IConfiguration settings, IDictionary variables)
    {
        var images = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in variables)
        {
            if (entry.Key is string name && name.StartsWith(LoggingVariablePrefix, StringComparison.OrdinalIgnoreCase) && entry.Value is string value)
                images[name.Replace("__", ":", StringComparison.Ordinal)] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(images).AddConfiguration(settings).Build().GetSection("Logging");
    }

    /// <summary>Counts every incoming MCP message, once however many features ask for it.</summary>
    private static void AddActivity(IServiceCollection services)
    {
        if (services.Any(service => service.ServiceType == typeof(McpActivity)))
            return;

        services.AddSingleton(provider => new McpActivity(TimeOf(provider)));
        services.AddOptions<McpServerOptions>()
            .PostConfigure<McpActivity>((options, activity) => options.Filters.Message.IncomingFilters.Add(activity.Hearing));
    }

    /// <summary>The clock the services hold, or the system's when they hold none.</summary>
    private static TimeProvider TimeOf(IServiceProvider provider) => provider.GetService<TimeProvider>() ?? TimeProvider.System;

    /// <summary>Marks the idle trim registered, so a second call adds nothing.</summary>
    private sealed class IdleTrimRegistered;

    /// <summary>The period and the veto of the idle shutdown, as the last call set them.</summary>
    private sealed record IdleShutdownPlan(TimeSpan After, Func<IServiceProvider, bool>? IsBusy);
}

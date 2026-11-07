using System.Reflection;
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
/// loaded; the server's own settings are, its settings file and its variables (<see cref="ServerSettings"/>). stdout carries JSON-RPC, so log lines go to stderr. The heap is
/// compacted once the server has gone quiet (<c>AddIdleTrim</c>); a server that wants its orphaned process ended adds
/// <c>AddIdleShutdown</c>.
/// </remarks>
public static class McpHost
{
    /// <summary>An empty host builder that logs from information up to stderr and trims the heap when the server is idle.</summary>
    /// <remarks>
    /// For a program built on Snail.Sdk.Mcp, which names its server in its assembly, the server's settings are read — and,
    /// first, its folders moved from the snail names (<see cref="ServerFolders.MoveFromSnailNames(string)"/>), so a
    /// settings file kept under the old name is found under the new one by this very start.
    /// </remarks>
    public static HostApplicationBuilder CreateBuilder(string[] args) => CreateBuilder(args, Assembly.GetEntryAssembly());

    /// <summary>The host builder of the server built into <paramref name="server"/>.</summary>
    internal static HostApplicationBuilder CreateBuilder(string[] args, Assembly? server)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

        if (ServerSettings.NameOf(server) is { } name)
            builder.Configuration.AddServerSettings(name);

        builder.Logging.UseStderr();
        builder.Services.AddIdleTrim();

        return builder;
    }

    extension(ILoggingBuilder logging)
    {
        /// <summary>Clears every logger and writes a line to stderr from information up.</summary>
        public ILoggingBuilder UseStderr()
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(new StderrLogging());

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
                TimeProvider.System,
                () => GC.GetTotalAllocatedBytes(),
                IdleTrim.Compact,
                provider.GetRequiredService<ILogger<IdleTrim>>()));

            return services;
        }

        /// <summary>Stops a stdio server that has heard no MCP message for <paramref name="after"/>, unless <paramref name="isBusy"/> says it is busy.</summary>
        /// <param name="after">The quiet period; <see cref="TimeSpan.Zero"/> turns the shutdown off.</param>
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
        /// The period is the server's to choose, and its setting's: the host reads no variable for it. Called again, the
        /// later period and veto replace the earlier ones.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The period is negative.</exception>
        public IServiceCollection AddIdleShutdown(TimeSpan after, Func<IServiceProvider, bool>? isBusy = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(after, TimeSpan.Zero);

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
                    TimeProvider.System,
                    provider.GetRequiredService<IHostApplicationLifetime>(),
                    provider.GetRequiredService<ILogger<IdleShutdown>>());
            });

            return services;
        }
    }

    /// <summary>Counts every incoming MCP message, once however many features ask for it.</summary>
    private static void AddActivity(IServiceCollection services)
    {
        if (services.Any(service => service.ServiceType == typeof(McpActivity)))
            return;

        services.AddSingleton(new McpActivity(TimeProvider.System));
        services.AddOptions<McpServerOptions>()
            .PostConfigure<McpActivity>((options, activity) => options.Filters.Message.IncomingFilters.Add(activity.Hearing));
    }

    /// <summary>Marks the idle trim registered, so a second call adds nothing.</summary>
    private sealed class IdleTrimRegistered;

    /// <summary>The period and the veto of the idle shutdown, as the last call set them.</summary>
    private sealed record IdleShutdownPlan(TimeSpan After, Func<IServiceProvider, bool>? IsBusy);
}

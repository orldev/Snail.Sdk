using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The host a stdio MCP server runs in.</summary>
/// <remarks>
/// The empty one: the full builder's appsettings, their file watchers, the command line and the default loggers are not
/// loaded; the server's own settings are, its settings file and its variables (<see cref="ServerSettings"/>). stdout carries JSON-RPC, so log lines go to stderr. The heap is
/// compacted once the server has gone quiet (<c>AddIdleTrim</c>).
/// </remarks>
public static class McpHost
{
    /// <summary>An empty host builder that logs from information up to stderr and trims the heap when the server is idle.</summary>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

        if (ServerSettings.NameOf(Assembly.GetEntryAssembly()) is { } name)
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
            if (services.Any(service => service.ServiceType == typeof(McpActivity)))
                return services;

            services.AddSingleton(new McpActivity(TimeProvider.System));
            services.AddOptions<McpServerOptions>()
                .PostConfigure<McpActivity>((options, activity) => options.Filters.Message.IncomingFilters.Add(activity.Hearing));
            services.AddHostedService(provider => new IdleTrim(
                provider.GetRequiredService<McpActivity>(),
                IdleTrim.PeriodOf(Environment.GetEnvironmentVariable(IdleTrim.Variable)),
                TimeProvider.System,
                () => GC.GetTotalAllocatedBytes(),
                IdleTrim.Compact,
                provider.GetRequiredService<ILogger<IdleTrim>>()));

            return services;
        }
    }
}

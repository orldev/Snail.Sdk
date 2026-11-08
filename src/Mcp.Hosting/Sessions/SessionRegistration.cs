using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>Puts a session's address to work: the tools it was granted held at the door, and the roots it works in.</summary>
public static class SessionRegistration
{
    extension(IMcpServerBuilder builder)
    {
        /// <summary>
        /// Refuses a call to a tool the session may not use (<see cref="IToolGate"/>, by default <see cref="SessionGrant"/>),
        /// and leaves such a tool out of the list the session is given.
        /// </summary>
        /// <remarks>
        /// <para>
        /// At the door, so every call the protocol carries passes the same check whichever tool it names. The door sees
        /// only what arrives through the protocol: a tool that invokes another runs past it, so a forwarder calls its steps
        /// through <see cref="ToolForwarding.CallAsync"/>, which asks the same gate. A role granted <c>git_status</c> and
        /// <c>run_tools</c> committed and pushed through Snail.MCP.Git until its forwarders did.
        /// </para>
        /// <para>
        /// Registers <see cref="SessionGrant"/>, the default gate, and an address that answers nothing
        /// (<see cref="SessionAddress.None"/>) unless the transport gives one (<c>WithLocalHttpTransport</c> or
        /// <c>WithSessionAddress</c> of <c>Snail.Sdk.Mcp.Http</c>). Called before or after those alike: the transport's
        /// address replaces this one wherever it comes, and this one is added only where there is none. A gate the server
        /// registers itself, before or after, is the one asked. Called twice, it registers once.
        /// </para>
        /// </remarks>
        public IMcpServerBuilder RefuseWhatWasNotGranted(Action<GrantOptions>? configure = null)
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(GrantOptions)))
                return builder;

            var options = new GrantOptions();
            configure?.Invoke(options);

            builder.Services.AddSingleton(options);
            builder.Services.TryAddSingleton(SessionAddress.None);
            builder.Services.TryAddSingleton(provider => new SessionGrant(provider.GetRequiredService<ISessionAddress>()));
            builder.Services.TryAddSingleton<IToolGate>(provider => new GrantGate(provider.GetRequiredService<GrantOptions>()));
            builder.WithRequestFilters(filters =>
            {
                filters.AddCallToolFilter(Refusing);

                if (options.HidesWithheldTools)
                    filters.AddListToolsFilter(Hiding);
            });

            return builder;
        }

        /// <summary>
        /// <see cref="SessionRoots"/> for the tools: the roots the session's address names inside the
        /// <paramref name="volumes"/> the server was configured with, or those volumes when it names none.
        /// </summary>
        /// <remarks>
        /// The volumes are read once, when the roots are first asked for; the address on every question. The same function
        /// given to <c>RefuseSessionsItCannotServe</c> of <c>Snail.Sdk.Mcp.Http</c> turns away at the door a session whose
        /// root this machine does not have or the volumes do not hold. Like <see cref="RefuseWhatWasNotGranted"/>, it is
        /// called before or after <c>WithLocalHttpTransport</c> or <c>WithSessionAddress</c> alike. Called twice, it
        /// registers once.
        /// </remarks>
        /// <param name="volumes">The folders the server serves, read from its services: its options.</param>
        /// <param name="configure">The server's own words — the setting that configures its roots — where the fleet's do not fit.</param>
        public IMcpServerBuilder WithSessionRoots(Func<IServiceProvider, IReadOnlyList<string>> volumes, Action<SessionRootsOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(volumes);

            if (builder.Services.Any(service => service.ServiceType == typeof(SessionRoots)))
                return builder;

            var options = new SessionRootsOptions();
            configure?.Invoke(options);

            builder.Services.AddSingleton(options);
            builder.Services.TryAddSingleton(SessionAddress.None);
            builder.Services.AddSingleton(provider => new SessionRoots(provider.GetRequiredService<ISessionAddress>(), volumes(provider), options));

            return builder;
        }
    }

    /// <summary>The services a request is served with: its own scope where there is one, the session's otherwise.</summary>
    internal static IServiceProvider? ServicesOf(MessageContext context) => context.Services ?? context.Server.Services;

    /// <summary>The call handed on when the gate lets it through, the refusal in the server's shape otherwise.</summary>
    private static McpRequestHandler<CallToolRequestParams, CallToolResult> Refusing(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            var services = ServicesOf(context);

            if (context.Params?.Name is not { } tool || services?.GetService<IToolGate>()?.Refusal(tool, services) is not { } refusal)
                return await next(context, cancellationToken).ConfigureAwait(false);

            return services.GetRequiredService<GrantOptions>().Refused(tool, refusal);
        };

    /// <summary>The list with every tool the gate would refuse left out.</summary>
    private static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Hiding(McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) =>
        async (context, cancellationToken) =>
        {
            var listed = await next(context, cancellationToken).ConfigureAwait(false);
            var services = ServicesOf(context);

            if (services?.GetService<IToolGate>() is not { } gate)
                return listed;

            listed.Tools = [.. listed.Tools.Where(tool => gate.Refusal(tool.Name, services) is null)];

            return listed;
        };
}

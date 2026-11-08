using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>Puts <c>run_program</c> on a server: a JavaScript program over its commands, in one call.</summary>
public static class ProgramRegistration
{
    extension(IMcpServerBuilder builder)
    {
        /// <summary>
        /// Registers the tool <c>run_program(code, input, timeoutSeconds)</c>, marked <see cref="Hosting.Sessions.ForwardsAttribute"/>,
        /// whose programs call the commands of <typeparamref name="TCommands"/> on the global object
        /// <see cref="ProgramOptions.Global"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <typeparamref name="TCommands"/> is resolved from the services of each request, registered scoped unless the server
        /// registered it itself; <see cref="ToolCommands"/> is made over the server's own tools instead. Before every command
        /// the gate of <c>RefuseWhatWasNotGranted</c> is asked for the command's tool, and a refusal reaches the program as an
        /// exception it may catch. The tool is destructive and open-world, since a program reaches whatever its commands reach.
        /// </para>
        /// <para>
        /// The options are checked here, so a name that cannot be a JavaScript identifier, a timeout under a second or a ceiling
        /// of zero fails at startup rather than on every call; a timeout past its ceiling raises the ceiling to it. Called
        /// twice, it registers once.
        /// </para>
        /// </remarks>
        public IMcpServerBuilder WithPrograms<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCommands>(Action<ProgramOptions>? configure = null)
            where TCommands : class, IProgramCommands
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(ProgramOptions)))
                return builder;

            var options = new ProgramOptions();
            configure?.Invoke(options);
            options.Check();

            var runner = new ProgramRunner(options);
            var commandsOf = CommandsOf<TCommands>(options);

            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(runner);

            if (typeof(TCommands) != typeof(ToolCommands))
                builder.Services.TryAddScoped<TCommands>();

            builder.Services.AddSingleton(services => Tool(new ProgramTool(options, runner, commandsOf), services));

            return builder;
        }
    }

    /// <summary>How a request finds its commands: the server's own catalog from its services, or its tools.</summary>
    private static Func<RequestContext<CallToolRequestParams>, IProgramCommands> CommandsOf<TCommands>(ProgramOptions options)
        where TCommands : class, IProgramCommands
    {
        if (typeof(TCommands) == typeof(ToolCommands))
            return context => new ToolCommands(context, $"{options.Global}_");

        return context => (context.Services ?? context.Server.Services)!.GetRequiredService<TCommands>();
    }

    private static McpServerTool Tool(ProgramTool program, IServiceProvider services)
    {
        var options = new McpServerToolCreateOptions
        {
            Name = ProgramTool.Name,
            Description = program.Description,
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
            OpenWorld = true,
            Services = services,
            SerializerOptions = McpJsonUtilities.DefaultOptions,
        };

        return McpServerTool.Create(program.RunAsync, options);
    }
}

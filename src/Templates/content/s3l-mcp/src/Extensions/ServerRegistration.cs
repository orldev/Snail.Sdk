using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Snail.MCP.Template.Tools;
using Snail.Sdk.Mcp.Hosting;
#if (jobs)
using Snail.Sdk.Mcp.Hosting.Jobs;
#endif

namespace Snail.MCP.Template.Extensions;

/// <summary>What the server says about itself and which tools it offers, the same over every transport.</summary>
public static class ServerRegistration
{
    /// <summary>The words a client shows the model before any tool: what the server is for and where to start.</summary>
    public const string Instructions = "TEMPLATE_DESCRIPTION Start with tmplshort_diagnose.";

    /// <summary>The server's name, version and instructions.</summary>
    public static void Describe(McpServerOptions server)
    {
        server.ServerInfo = ServerIdentity.Of(typeof(ServerRegistration).Assembly);
        server.ServerInstructions = Instructions;
    }

    extension(IMcpServerBuilder builder)
    {
        /// <summary>Every tool of the server; a native build keeps the methods of the classes named here.</summary>
#if (jobs)
        /// <remarks>The jobs of the server beside its tools: tmplshort_work starts one, and a client reads or awaits it as a resource.</remarks>
        public IMcpServerBuilder WithServerTools() => builder.WithTools<DiagnosticsTools>().WithTools<WorkTools>(ToolJson.Options(WorkJson.Default)).WithJobs("tmplshort");
#else
        public IMcpServerBuilder WithServerTools() => builder.WithTools<DiagnosticsTools>();
#endif
    }
}

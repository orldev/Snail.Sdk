using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Snail.MCP.Template.Tools;
using Snail.Sdk.Mcp.Hosting;
#if (UsesHttp)
using Snail.Sdk.Mcp.Hosting.Answers;
#endif
#if (jobs)
using Snail.Sdk.Mcp.Hosting.Jobs;
#endif
#if (UsesHttp)
using Snail.Sdk.Mcp.Hosting.Sessions;
#endif

namespace Snail.MCP.Template.Extensions;

/// <summary>What the server says about itself and which tools it offers, the same over every transport.</summary>
public static class ServerRegistration
{
    /// <summary>The words a client shows the model before any tool: what the server is for, where to start, and how it answers.</summary>
    public const string Instructions = "TEMPLATE_DESCRIPTION Start with tmplshort_diagnose. Every answer is JSON with an ok flag, and a failed call is also flagged isError. When ok is false, read error and hint and fix the call instead of retrying it verbatim.";

    /// <summary>The server's name, version and instructions.</summary>
    public static void Describe(McpServerOptions server)
    {
        server.ServerInfo = ServerIdentity.Of(typeof(ServerRegistration).Assembly);
        server.ServerInstructions = Instructions;
    }
#if (UsesHttp)

    /// <summary>A call to a tool the session's address did not grant is refused in the envelope every answer of the server is.</summary>
    public static void Grant(GrantOptions grant) =>
        grant.Refusal = (_, words) => ToolAnswer.Failed(words, "Use the tools this session lists; another needs a session opened with it granted.", "not_granted");
#endif

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

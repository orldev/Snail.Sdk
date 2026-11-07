using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.Template.Extensions;

#if (container)
/// <summary>One server for every session reaching the container: the port of its URL on every interface, the published port the boundary.</summary>
#else
/// <summary>One server for every session of the machine, on a loopback address, kept running by launchd.</summary>
#endif
/// <remarks>
/// The host — loopback, or every interface inside a container — the refusal of browsers and the idle trim are
/// Snail.Sdk.Mcp.Http's. A platform that gives a role some of the tools names them in the address it opens the session
/// at, <c>/mcp?tool=tmplshort_diagnose</c>: a tool left out is not listed, and a call to it is refused.
/// </remarks>
public static class HttpServerHost
{
    /// <summary>Runs the server over Streamable HTTP until the system ends it.</summary>
    public static Task RunAsync(string[] args)
    {
        var builder = McpHttpHost.CreateBuilder(args, ServerSettings.Endpoint);

        builder.Services.AddMcpServer(ServerRegistration.Describe)
            .ExplainFailures()
            .WithLocalHttpTransport()
            .RefuseWhatWasNotGranted()
            .WithServerTools();

        var app = builder.Build();

        app.MapLocalMcp();
        app.MapHealth();

        return app.RunAsync();
    }
}

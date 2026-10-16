using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Http;

namespace Snail.MCP.Template.Extensions;

#if (container)
/// <summary>One server for every session reaching the container: the port of its URL on every interface, the published port the boundary.</summary>
/// <remarks>The host, the refusal of browsers and the idle trim are Snail.Sdk.Mcp.Http's.</remarks>
#else
/// <summary>One server for every session of the machine, on a loopback address, kept running by launchd.</summary>
/// <remarks>The loopback address and the refusal of browsers are Snail.Sdk.Mcp.Http's.</remarks>
#endif
public static class HttpServerHost
{
    /// <summary>Runs the server over Streamable HTTP until the system ends it.</summary>
    public static Task RunAsync(string[] args)
    {
#if (container)
        var builder = McpHttpHost.CreateContainerBuilder(args, ServerSettings.Url.Port);
#else
        var builder = McpHttpHost.CreateBuilder(args, ServerSettings.Url);
#endif

        builder.Services.AddMcpServer(ServerRegistration.Describe).WithLocalHttpTransport().WithServerTools();

        var app = builder.Build();

        app.MapLocalMcp();

        return app.RunAsync();
    }
}

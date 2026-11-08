#if (UsesStdio)
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Answers;

#endif
namespace Snail.MCP.Template.Extensions;

/// <summary>The server as a running host, on the transport it was made for.</summary>
public static class ServerHost
{
    /// <summary>Runs the server until the client or the system ends it.</summary>
    public static Task RunAsync(string[] args)
    {
#if (IsBoth)
        if (ServerSettings.Endpoint.Transport is McpTransport.Http)
            return HttpServerHost.RunAsync(args);

#endif
#if (UsesStdio)
#if (UsesHttp)
        var builder = McpHost.CreateBuilder(args, ServerSettings.Values);
#else
        var builder = McpHost.CreateBuilder(args);
#endif

        builder.Services.AddMcpServer(ServerRegistration.Describe).ExplainFailures().WithStdioServerTransport().WithServerTools();

        return builder.Build().RunAsync();
#else
        return HttpServerHost.RunAsync(args);
#endif
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Probe;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Http;

if (Environment.GetEnvironmentVariable("SNAIL_MCP_PROBE_FAIL") is { Length: > 0 } failure)
{
    await Console.Error.WriteLineAsync(failure);

    return 3;
}

var endpoint = McpEndpoint.Read(ServerSettings.Read("probe"), new Uri("http://127.0.0.1:9/"));

if (endpoint.Transport is McpTransport.Stdio)
{
    await Console.Error.WriteLineAsync("the probe was told to answer over stdio");

    return 4;
}

var builder = McpHttpHost.CreateBuilder(args, endpoint);

builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer().WithLocalHttpTransport().WithTools<ProbeTools>();

var app = builder.Build();

app.MapLocalMcp();
app.MapGet("/healthz", Probe.Healthy);
app.MapGet("/unready", Probe.Unready);

await Console.Error.WriteLineAsync($"listening on {endpoint.Url}");
await app.RunAsync();

return 0;

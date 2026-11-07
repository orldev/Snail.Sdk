using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Probe;
using Snail.Sdk.Mcp.Http;

if (Environment.GetEnvironmentVariable("SNAIL_MCP_PROBE_FAIL") is { Length: > 0 } failure)
{
    await Console.Error.WriteLineAsync(failure);

    return 3;
}

var url = Environment.GetEnvironmentVariable("SNAIL_MCP_PROBE_URL") ?? $"http://127.0.0.1:{Environment.GetEnvironmentVariable("SNAIL_MCP_PROBE_PORT")}/";
var builder = McpHttpHost.CreateBuilder(args, new Uri(url));

builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer().WithLocalHttpTransport().WithTools<ProbeTools>();

var app = builder.Build();

app.MapLocalMcp();
app.MapGet("/healthz", Probe.Healthy);
app.MapGet("/unready", Probe.Unready);

await Console.Error.WriteLineAsync($"listening on {url}");
await app.RunAsync();

return 0;

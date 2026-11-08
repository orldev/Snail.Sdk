using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Snail.MCP.Probe;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Http;

if (Environment.GetEnvironmentVariable("SNAIL_MCP_PROBE_FAIL") is { Length: > 0 } failure)
{
    await Console.Error.WriteLineAsync(failure);

    return 3;
}

var settings = ServerSettings.Read("probe");
var endpoint = McpEndpoint.Read(settings, new Uri("http://127.0.0.1:9/"));

if (endpoint.Transport is McpTransport.Stdio)
{
    await Console.Error.WriteLineAsync("the probe was told to answer over stdio");

    return 4;
}

var limits = new ProbeLimits();

try
{
    ServerSettings.Bind("probe", settings, read => read.Bind(limits));
}
catch (StartRefusedException refused)
{
    await Console.Error.WriteLineAsync(refused.Message);

    return 1;
}

var builder = McpHttpHost.CreateBuilder(args, endpoint);

builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer().WithLocalHttpTransport().WithTools<ProbeTools>();
builder.Services.AddOptions<ProbeOptions>().Bind(settings).ValidateOnStart();

if (string.Equals(settings["TokenRequired"], "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddOptions<ProbeOptions>()
        .Configure(options => options.Token = settings["Token"])
        .Validate(options => options.Token is { Length: > 0 }, $"{ServerSettings.NameOfSetting("probe", "Token")} is not set; the probe asks for one.")
        .ValidateOnStart();
}

var app = builder.Build();

app.MapLocalMcp();
app.MapGet("/healthz", Probe.Healthy);
app.MapGet("/unready", Probe.Unready);

await Console.Error.WriteLineAsync($"listening on {endpoint.Url}");

try
{
    await app.StartAsync();
}
catch (StartRefusedException refused)
{
    await Console.Error.WriteLineAsync(refused.Message);

    return 1;
}

await app.WaitForShutdownAsync();

return 0;

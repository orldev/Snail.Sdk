using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

public sealed class HttpGuardsTests
{
    [Fact]
    public async Task Problems_OfTheHostOfSnailSdkMcpHttp_AreNone()
    {
        await using var host = await HostAsync(app => app.MapLocalMcp());

        Assert.Empty(await HttpGuards.ProblemsAsync(host.Endpoint));
    }

    /// <summary>A host built without MapLocalMcp — the one Snail.MCP.Documents built for its image — lets a page in a browser through.</summary>
    [Fact]
    public async Task Problems_OfAHostWithoutTheRefusal_NameTheBrowserLetThrough()
    {
        await using var host = await HostAsync(app => app.MapMcp(McpHttpHost.DefaultPath));

        var problems = await HttpGuards.ProblemsAsync(host.Endpoint);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("Origin: https://example.com was answered", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("Origin: null was answered", StringComparison.Ordinal));
    }

    /// <summary>An endpoint that refuses everyone refuses a browser too, which proves nothing.</summary>
    [Fact]
    public async Task Problems_OfAHostThatRefusesEveryone_NameTheClientRefused()
    {
        await using var host = await HostAsync(app => app.Use(RefuseEveryone));

        var problem = Assert.Single(await HttpGuards.ProblemsAsync(host.Endpoint));

        Assert.Contains("without Origin was answered 403", problem, StringComparison.Ordinal);
    }

    private static Task RefuseEveryone(HttpContext context, RequestDelegate next)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    }

    private static async Task<Host> HostAsync(Action<WebApplication> map)
    {
        var url = new Uri($"http://127.0.0.1:{Ports.Free()}/");
        var builder = McpHttpHost.CreateBuilder([], url);

        builder.Services.AddMcpServer().WithLocalHttpTransport();

        var app = builder.Build();

        map(app);
        await app.StartAsync();

        return new Host(app, new Uri(url, McpHttpHost.DefaultPath));
    }

    private sealed record Host(WebApplication App, Uri Endpoint) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }
}

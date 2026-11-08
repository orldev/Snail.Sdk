using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

public sealed class HttpGuardsTests
{
    private const string Key = "studio-token";

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

    /// <summary>Snail.MCP.Blender and AfterEffects keep their endpoint behind a token: a browser holds none, so it is answered 401.</summary>
    [Fact]
    public async Task AuthorizedProblems_OfAnEndpointBehindAKey_AreNone()
    {
        await using var host = await HostAsync(app =>
        {
            app.Use(RequireTheKey);
            app.MapMcp(McpHttpHost.DefaultPath);
        });

        Assert.Empty(await HttpGuards.AuthorizedProblemsAsync(host.Endpoint, new AuthenticationHeaderValue("Bearer", Key)));
    }

    /// <summary>The Origin refusal joined before the key holds too: a browser is answered 403 rather than 401.</summary>
    [Fact]
    public async Task AuthorizedProblems_OfAnEndpointBehindTheRefusalAndAKey_AreNone()
    {
        await using var host = await HostAsync(app =>
        {
            app.UseBrowserRefusal();
            app.Use(RequireTheKey);
            app.MapMcp(McpHttpHost.DefaultPath);
        });

        Assert.Empty(await HttpGuards.AuthorizedProblemsAsync(host.Endpoint, new AuthenticationHeaderValue("Bearer", Key)));
    }

    [Fact]
    public async Task AuthorizedProblems_OfAnEndpointWithoutAKey_NameEveryoneLetIn()
    {
        await using var host = await HostAsync(app => app.MapMcp(McpHttpHost.DefaultPath));

        var problems = await HttpGuards.AuthorizedProblemsAsync(host.Endpoint, new AuthenticationHeaderValue("Bearer", Key));

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("without a key was answered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthorizedProblems_OfAnEndpointThatRefusesTheKey_NameTheClientRefused()
    {
        await using var host = await HostAsync(app => app.Use(RefuseEveryone));

        var problem = Assert.Single(await HttpGuards.AuthorizedProblemsAsync(host.Endpoint, new AuthenticationHeaderValue("Bearer", Key)));

        Assert.Contains("with the key was answered 403", problem, StringComparison.Ordinal);
    }

    private static Task RequireTheKey(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Headers.Authorization == $"Bearer {Key}")
            return next(context);

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        return Task.CompletedTask;
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

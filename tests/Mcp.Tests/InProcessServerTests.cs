using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>A server in the test's process on a port it knows before it starts, and one that keeps a whole WebApplication of its own.</summary>
public sealed class InProcessServerTests
{
    /// <summary>Snail.MCP.Blender signs links with its address as its services are built, so the port is the test's choice.</summary>
    [Fact]
    public async Task StartAsync_AGivenPort_ListensOnIt()
    {
        var port = Ports.Free();

        await using var server = await InProcessServer.StartAsync(builder => builder.Services.AddMcpServer().WithLocalHttpTransport(), port: port);
        await using var session = await server.SessionAsync();

        Assert.Equal(new Uri($"http://127.0.0.1:{port}/"), server.Address);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(70_000)]
    public async Task StartAsync_APortThatIsNoPort_IsRefused(int port) =>
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => InProcessServer.StartAsync(_ => { }, port: port));

    /// <summary>The shape of Snail.MCP.Blender's host: the full builder, the endpoint's address, the MCP endpoint behind the refusal.</summary>
    [Fact]
    public async Task ServeAsync_AWholeWebApplicationAtTheEndpoint_IsServed()
    {
        var port = Ports.Free();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.UseMcpEndpoint(new McpEndpoint(McpTransport.Http, new Uri($"http://127.0.0.1:{port}/"), ListensOnEveryInterface: false));
        builder.Services.AddMcpServer().WithLocalHttpTransport();
        var app = builder.Build();
        app.MapLocalMcp();

        await using var server = await InProcessServer.ServeAsync(app);
        await using var session = await server.SessionAsync();

        Assert.Equal(port, server.Address.Port);
        Assert.Empty(await HttpGuards.ProblemsAsync(server.Endpoint));
    }
}

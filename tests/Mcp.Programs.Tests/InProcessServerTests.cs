using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Programs.Tests;

/// <summary>run_program over HTTP in the test's own process: the grant read from the session's query, on a port Kestrel chose.</summary>
public sealed class InProcessServerTests
{
    [Fact]
    public async Task Program_OverHttp_IsHeldToTheGrantOfItsSessionsQuery()
    {
        var notes = new Notes();
        await using var server = await InProcessServer.StartAsync(builder =>
        {
            builder.Services.AddSingleton(notes);
            builder.Services.AddMcpServer()
                .WithLocalHttpTransport()
                .RefuseWhatWasNotGranted()
                .ExplainFailures()
                .WithTools<NoteTools>()
                .WithPrograms<NoteCommands>(options => options.Global = "notes");
        });
        await using var session = await server.SessionAsync("tool=notes_read&tool=run_program");

        var answer = await session.CallToolAsync("run_program", new Dictionary<string, object?>
        {
            ["code"] = "notes.read(); try { notes.write({ text: 'x' }); } catch (e) { return e.type; }",
        });

        Assert.True(answer.IsOk(), answer.Text());
        Assert.Equal("notGranted", answer.Data()!["result"]!.GetValue<string>());
        Assert.Empty(notes.Written);
        Assert.Equal("127.0.0.1", server.Address.Host);
        Assert.NotEqual(0, server.Address.Port);
        Assert.Equal(new Uri(server.Address, "mcp"), server.Endpoint);
    }

    /// <summary>A server whose program builds its host itself is served as built, at the path it maps.</summary>
    [Fact]
    public async Task HostTheServerBuilt_IsServedAtItsOwnPath()
    {
        var builder = McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:0/"), false));

        builder.Services.AddSingleton(new Notes());
        builder.Services.AddMcpServer().WithLocalHttpTransport().WithTools<NoteTools>();

        var app = builder.Build();
        app.MapLocalMcp("/notes/mcp");

        await using var server = await InProcessServer.ServeAsync(app, "/notes/mcp");
        await using var session = await server.SessionAsync();

        var tools = await session.ListToolsAsync();

        Assert.Equal(new Uri(server.Address, "notes/mcp"), server.Endpoint);
        Assert.Contains(tools, tool => tool.Name == "notes_read");
        Assert.Contains(server.Services.GetServices<McpServerTool>(), tool => tool.ProtocolTool.Name == "notes_write");
    }
}

using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.Testing;

#pragma warning disable MCP9005

namespace Snail.Sdk.Mcp.Tests;

public sealed class McpHttpHostTests : IAsyncLifetime
{
    private readonly Uri _url = new($"http://127.0.0.1:{FreePort()}/");
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        var builder = McpHttpHost.CreateBuilder([], _url);

        builder.Services.AddMcpServer().WithLocalHttpTransport().WithTools<RootsProbe>();

        _app = builder.Build();
        _app.MapLocalMcp();

        await _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void AnAddressBeyondThisMachineIsRefused() =>
        Assert.Throws<ArgumentException>(() => McpHttpHost.CreateBuilder([], new Uri("http://0.0.0.0:9230/")));

    [Fact]
    public async Task ARequestFromABrowserIsRefused()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_url, McpHttpHost.DefaultPath))
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("Origin", "https://example.com");

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>The sessionless revision is refused in the way that makes a client of two revisions fall back to initialize, and with it to a session.</summary>
    [Fact]
    public async Task ARequestWithoutASessionIsSentBackToInitialize()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_url, McpHttpHost.DefaultPath))
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"server/discover","params":{"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"claude-code","version":"2.1.296"},"io.modelcontextprotocol/clientCapabilities":{"roots":{"listChanged":true}}}}}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };

        request.Headers.Add("Accept", "application/json, text/event-stream");
        request.Headers.Add("mcp-protocol-version", "2026-07-28");
        request.Headers.Add("mcp-method", "server/discover");

        using var response = await http.SendAsync(request);
        var answer = await response.Content.ReadAsStringAsync();

        Assert.Contains("-32022", answer, StringComparison.Ordinal);
        Assert.Contains("2025-11-25", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClientsSessionLetsTheServerAskItsRoots()
    {
        var options = new McpClientOptions
        {
            ProtocolVersion = "2025-06-18",
            Capabilities = new ClientCapabilities { Roots = new RootsCapability() },
            Handlers = new McpClientHandlers
            {
                RootsHandler = (_, _) => ValueTask.FromResult(new ListRootsResult { Roots = [new Root { Uri = "file:///work/Probe" }] }),
            },
        };

        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(_url, McpHttpHost.DefaultPath) }),
            options);

        var answer = await client.CallToolAsync("first_root");

        Assert.Equal("file:///work/Probe", answer.Text());
    }

    /// <summary>A message to any HTTP session keeps the whole server awake, as one over stdio does.</summary>
    [Fact]
    public async Task Message_OverHttp_StartsTheQuietAgain()
    {
        var activity = _app.Services.GetRequiredService<McpActivity>();

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var quietBefore = activity.Quiet;

        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(_url, McpHttpHost.DefaultPath) }));

        Assert.True(activity.Quiet < quietBefore);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [McpServerToolType]
    private sealed class RootsProbe
    {
        [McpServerTool(Name = "first_root"), Description("The first root the client lists.")]
        public static async Task<string> FirstRoot(McpServer server, CancellationToken cancellationToken) =>
            (await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken)).Roots[0].Uri;
    }
}

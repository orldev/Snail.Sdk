using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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
        _app.MapHealth();

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

    /// <summary>A container's healthcheck asks without an <c>Origin</c>, and hears 200 with nothing more to read.</summary>
    [Fact]
    public async Task TheHealthCheck_Answers200WithNoBody()
    {
        using var http = new HttpClient();

        using var response = await http.GetAsync(new Uri(_url, McpHttpHost.DefaultHealthPath));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(body);
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

    /// <summary>The web host reads <c>urls</c> from the configuration the services hold: Snail.MCP.Documents registered its own and got localhost:5000.</summary>
    [Fact]
    public async Task CreateBuilder_AServerWithAConfigurationOfItsOwn_StillListensWhereAsked()
    {
        var port = FreePort();
        var builder = McpHttpHost.CreateBuilder([], new Uri($"http://127.0.0.1:{port}/"));

        builder.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        Assert.Equal(HttpStatusCode.OK, await HealthAsync(builder, port));
    }

    [Fact]
    public async Task CreateBuilder_Localhost_ListensOnLoopback()
    {
        var port = FreePort();

        var status = await HealthAsync(McpHttpHost.CreateBuilder([], new Uri($"http://localhost:{port}/")), port);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task CreateBuilder_AnEndpointOnEveryInterface_ListensOnItsPort()
    {
        var port = FreePort();
        var endpoint = new McpEndpoint(McpTransport.Http, new Uri($"http://0.0.0.0:{port}/"), ListensOnEveryInterface: true);

        var status = await HealthAsync(McpHttpHost.CreateBuilder([], endpoint), port);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    /// <summary>The host serves plain HTTP: an https address would start a host that answers nobody.</summary>
    [Fact]
    public void CreateBuilder_AnHttpsAddress_IsRefused() =>
        Assert.Throws<ArgumentException>(() => McpHttpHost.CreateBuilder([], new Uri("https://127.0.0.1:9230/")));

    /// <summary>Snail.MCP.Playwright: the process answers, but its browser does not, so the orchestrator hears 503 and restarts it.</summary>
    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task MapHealth_WithACheck_AnswersAsItSays(bool isHealthy, HttpStatusCode expected)
    {
        var (status, body, _) = await CheckedHealthAsync((_, _) => Task.FromResult(new HealthAnswer(isHealthy)));

        Assert.Equal(expected, status);
        Assert.Empty(body);
    }

    [Fact]
    public async Task MapHealth_ACheckThatThrows_Answers503WithNoBody()
    {
        var (status, body, _) = await CheckedHealthAsync((_, _) => throw new InvalidOperationException("The browser is gone."));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Empty(body);
    }

    [Fact]
    public async Task MapHealth_TheCheck_IsAskedWithTheServersServices()
    {
        var (status, _, _) = await CheckedHealthAsync((services, _) => Task.FromResult(new HealthAnswer(services.GetService<McpActivity>() is not null)));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    /// <summary>Snail.MCP.Blender's /healthz is a contract with a body, and says Blender is down while the server itself is healthy.</summary>
    [Fact]
    public async Task MapHealth_ABody_IsAnsweredAsJson()
    {
        var (status, body, type) = await CheckedHealthAsync((_, _) => Task.FromResult(new HealthAnswer(true, new JsonObject { ["status"] = "ok", ["blender"] = "down" })));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"status":"ok","blender":"down"}""", body);
        Assert.Equal("application/json", type);
    }

    /// <summary>A server an orchestrator should restart says why in the body of its 503.</summary>
    [Fact]
    public async Task MapHealth_AnUnhealthyAnswerWithABody_Answers503WithIt()
    {
        var (status, body, _) = await CheckedHealthAsync((_, _) => Task.FromResult(new HealthAnswer(false, new JsonObject { ["status"] = "stalled" })));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("""{"status":"stalled"}""", body);
    }

    [Fact]
    public void MapHealth_ABlankPath_IsRefused() =>
        Assert.Throws<ArgumentException>(() => _app.MapHealth(" ", (_, _) => Task.FromResult(new HealthAnswer(true))));

    /// <summary>The refusal holds for the MCP endpoint: Snail.MCP.Figma's plugin sends Origin: null to the bridge, AfterEffects' pages post forms with an Origin.</summary>
    [Theory]
    [InlineData("/mcp", HttpStatusCode.Forbidden)]
    [InlineData("/MCP", HttpStatusCode.Forbidden)]
    [InlineData("/mcp/sse", HttpStatusCode.Forbidden)]
    [InlineData("/mcpx", HttpStatusCode.OK)]
    [InlineData("/page", HttpStatusCode.OK)]
    public async Task MapLocalMcp_TheRefusal_HoldsForTheEndpointAlone(string path, HttpStatusCode expected)
    {
        var port = FreePort();
        var builder = McpHttpHost.CreateBuilder([], new Uri($"http://127.0.0.1:{port}/"));
        builder.Services.AddMcpServer().WithLocalHttpTransport();
        await using var app = builder.Build();
        app.MapLocalMcp();
        app.MapFallback(() => "page");
        await app.StartAsync();

        var status = await FromABrowserAsync(new Uri($"http://127.0.0.1:{port}{path}"), HttpMethod.Get);

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task MapLocalMcp_TheHealthCheck_AnswersABrowser() =>
        Assert.Equal(HttpStatusCode.OK, await FromABrowserAsync(new Uri(_url, McpHttpHost.DefaultHealthPath), HttpMethod.Get));

    /// <summary>Snail.MCP.Figma: the MCP endpoint and the bridge are two ports of one host, and only the MCP one refuses.</summary>
    [Fact]
    public async Task UseBrowserRefusal_OnAPort_LetsAnotherPortThrough()
    {
        var mcp = FreePort();
        var bridge = FreePort();
        var builder = McpHttpHost.CreateBuilder([], new Uri($"http://127.0.0.1:{mcp}/"));
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, bridge));
        await using var app = builder.Build();
        app.UseBrowserRefusal(McpHttpHost.DefaultPath, mcp);
        app.MapGet("/mcp", () => "answered");
        await app.StartAsync();

        Assert.Equal(HttpStatusCode.Forbidden, await FromABrowserAsync(new Uri($"http://127.0.0.1:{mcp}/mcp"), HttpMethod.Get));
        Assert.Equal(HttpStatusCode.OK, await FromABrowserAsync(new Uri($"http://127.0.0.1:{bridge}/mcp"), HttpMethod.Get));
    }

    [Theory]
    [InlineData("")]
    [InlineData("mcp")]
    public void UseBrowserRefusal_NotAPath_IsRefused(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => _app.UseBrowserRefusal(path));

    /// <summary>Snail.MCP.Blender keeps a whole WebApplication for its pages and sign-in, and wrote app.Urls by hand.</summary>
    [Fact]
    public async Task UseMcpEndpoint_AWholeWebApplication_ListensWhereTheEndpointSays()
    {
        var port = FreePort();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        builder.WebHost.UseUrls("http://127.0.0.1:1");

        builder.UseMcpEndpoint(new McpEndpoint(McpTransport.Http, new Uri($"http://127.0.0.1:{port}/"), ListensOnEveryInterface: false));

        Assert.Contains(builder.Services, service => service.ServiceType == typeof(McpActivity));
        Assert.Equal(HttpStatusCode.OK, await HealthAsync(builder, port));
    }

    [Fact]
    public void UseMcpEndpoint_AStdioEndpoint_IsRefused() =>
        Assert.Throws<ArgumentException>(() => WebApplication.CreateBuilder().UseMcpEndpoint(new McpEndpoint(McpTransport.Stdio, new Uri("http://127.0.0.1:9230/"), ListensOnEveryInterface: false)));

    /// <summary>Snail.MCP.Figma's stdio server serves its plugin's bridge on Kestrel, and repeated the host's lines for it.</summary>
    [Fact]
    public async Task CreateStdioBuilder_ListensOnlyWhereTheServerSays_OverItsSettings()
    {
        var port = FreePort();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Bridge:Port"] = $"{port}" }).Build();

        var builder = McpHttpHost.CreateStdioBuilder([], settings);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, int.Parse(builder.Configuration["Bridge:Port"]!, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Contains(builder.Services, service => service.ServiceType == typeof(McpActivity));
        Assert.Equal(HttpStatusCode.OK, await HealthAsync(builder, port));
    }

    /// <summary>Snail.MCP.Blender and AfterEffects listen on a LAN address behind their tokens; how far an endpoint reaches is the read's to decide.</summary>
    [Fact]
    public void CreateBuilder_AnEndpointAtALanAddress_IsBuilt()
    {
        var builder = McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Http, new Uri("http://192.168.1.5:9230/"), ListensOnEveryInterface: false), new ConfigurationBuilder().Build());

        Assert.NotNull(builder);
    }

    /// <summary>The settings the program read are the host's, read once — and an in-process test, under the runner, gives them the same way.</summary>
    [Fact]
    public async Task CreateBuilder_WithTheSettingsTheProgramRead_IsTheirHost()
    {
        var port = FreePort();
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BrowserDefaults:Headless"] = "true" })
            .Build();
        var endpoint = new McpEndpoint(McpTransport.Http, new Uri($"http://127.0.0.1:{port}/"), ListensOnEveryInterface: false);

        var builder = McpHttpHost.CreateBuilder([], endpoint, settings);

        Assert.Equal("true", builder.Configuration["BrowserDefaults:Headless"]);
        Assert.Equal(HttpStatusCode.OK, await HealthAsync(builder, port));
    }

    [Fact]
    public async Task CreateBuilder_WithSettings_AnEndpointOnEveryInterface_ListensOnItsPort()
    {
        var port = FreePort();
        var endpoint = new McpEndpoint(McpTransport.Http, new Uri($"http://0.0.0.0:{port}/"), ListensOnEveryInterface: true);

        var status = await HealthAsync(McpHttpHost.CreateBuilder([], endpoint, new ConfigurationBuilder().Build()), port);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Theory]
    [InlineData(McpTransport.Stdio, "http://127.0.0.1:9230/")]
    [InlineData(McpTransport.Http, "http://example.com:9230/")]
    [InlineData(McpTransport.Http, "https://127.0.0.1:9230/")]
    public void CreateBuilder_WithSettings_RefusesWhatTheEndpointOverloadRefuses(McpTransport transport, string url) =>
        Assert.Throws<ArgumentException>(() => McpHttpHost.CreateBuilder([], new McpEndpoint(transport, new Uri(url), ListensOnEveryInterface: false), new ConfigurationBuilder().Build()));

    private static async Task<(HttpStatusCode Status, string Body, string? Type)> CheckedHealthAsync(Func<IServiceProvider, CancellationToken, Task<HealthAnswer>> check)
    {
        var port = FreePort();
        await using var app = McpHttpHost.CreateBuilder([], new Uri($"http://127.0.0.1:{port}/")).Build();

        app.MapHealth(McpHttpHost.DefaultHealthPath, check);
        await app.StartAsync();

        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}{McpHttpHost.DefaultHealthPath}"));
        var body = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, body, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<HttpStatusCode> FromABrowserAsync(Uri address, HttpMethod method)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(method, address);

        request.Headers.Add("Origin", "null");

        using var response = await http.SendAsync(request);

        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> HealthAsync(WebApplicationBuilder builder, int port)
    {
        await using var app = builder.Build();

        app.MapHealth();
        await app.StartAsync();

        using var http = new HttpClient();
        using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}{McpHttpHost.DefaultHealthPath}"));

        return response.StatusCode;
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

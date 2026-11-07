using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Sessions;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The session's address read from each request, and the sessions a server cannot serve turned away at the door.</summary>
public sealed class HttpSessionsTests
{
    /// <summary>A server on the SDK's default transport has no session to remember an address in: each request carries its own.</summary>
    [Fact]
    public async Task Address_OnTheDefaultTransport_IsReadFromEachRequest()
    {
        await using var app = await StartAsync(builder => builder.WithHttpTransport().WithSessionAddress(), null);

        await using var first = await ConnectAsync(app, "?tool=read_note&tool=address");
        await using var second = await ConnectAsync(app, "?tool=list_notes");

        Assert.Equal("read_note, address", (await first.CallToolAsync("address")).Text());
        Assert.Equal("list_notes", (await second.CallToolAsync("address")).Text());
    }

    [Fact]
    public async Task Grant_OverHttp_HidesAndRefusesWhatTheAddressDidNotName()
    {
        await using var app = await StartAsync(builder => builder.WithLocalHttpTransport().RefuseWhatWasNotGranted(), null);

        await using var client = await ConnectAsync(app, "?tool=address");
        var listed = await client.ListToolsAsync();
        var refused = await client.CallToolAsync("read_note");

        Assert.Equal(["address"], listed.Select(tool => tool.Name));
        Assert.Equal("'read_note' was not granted to this session, which may use address", refused.Text());
    }

    [Fact]
    public async Task Grant_OverHttp_WhereOneIsRequired_RefusesASessionThatNamedNone()
    {
        await using var app = await StartAsync(builder => builder.WithLocalHttpTransport().RefuseWhatWasNotGranted(options => options.RequiresGrant = true), null);

        await using var client = await ConnectAsync(app, string.Empty);
        var refused = await client.CallToolAsync("address");

        Assert.Equal(true, refused.IsError);
    }

    [Fact]
    public async Task Door_LetsInASessionWhoseRootIsACopyInTheVolume()
    {
        var volume = TestFolder.Create();
        var copy = Directory.CreateDirectory(Path.Combine(volume, "task-1")).FullName;
        await using var app = await StartAsync(builder => builder.WithLocalHttpTransport(), _ => [volume]);

        await using var client = await ConnectAsync(app, $"?root={copy}");

        Assert.Equal("the note", (await client.CallToolAsync("read_note")).Text());
    }

    /// <summary>Discovery handed the worker of one machine the server of another, and nothing in an answer showed it.</summary>
    [Fact]
    public async Task Door_TurnsAwayASessionWhoseRootThisMachineDoesNotHave()
    {
        var volume = TestFolder.Create();
        await using var app = await StartAsync(builder => builder.WithLocalHttpTransport(), _ => [volume]);

        var (status, said) = await PostAsync(app, $"?root={volume}/task-elsewhere", origin: null);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal($"this machine has no {volume}/task-elsewhere: the session named a working copy that is not on the host this server runs on", said);
    }

    [Fact]
    public async Task Door_TurnsAwayASessionWhoseRootIsOutsideTheVolumes()
    {
        var volume = TestFolder.Create();
        var elsewhere = TestFolder.Create();
        await using var app = await StartAsync(builder => builder.WithLocalHttpTransport(), _ => [volume]);

        var (status, said) = await PostAsync(app, $"?root={elsewhere}", origin: null);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal($"this server serves the working copies under {volume}, and {elsewhere} is not one of them", said);
    }

    [Fact]
    public async Task Door_TurnsAwayASessionWithNoRoot_WhereNoVolumeIsConfigured()
    {
        await using var app = await StartAsync(builder => builder.WithLocalHttpTransport(), _ => []);

        var (status, said) = await PostAsync(app, string.Empty, origin: null);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.StartsWith("this session named no root and this server is configured with none", said, StringComparison.Ordinal);
    }

    /// <summary>Called before MapLocalMcp, the door still answers a browser as MapLocalMcp would: a 400 naming a missing folder tells a page which copies the machine has.</summary>
    [Fact]
    public async Task Door_AnswersABrowser403_BeforeItSaysAnythingAboutTheRoot()
    {
        var volume = TestFolder.Create();
        await using var app = await StartAsync(builder => builder.WithLocalHttpTransport(), _ => [volume]);

        var (status, said) = await PostAsync(app, $"?root={volume}/task-elsewhere", origin: "https://example.com");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Empty(said);
    }

    /// <summary>A server on the default transport, with the door in front of the endpoint when <paramref name="volumes"/> is given.</summary>
    /// <remarks>The door is put in before <c>MapLocalMcp</c>, the order in which it would leak most if it relied on the browser refusal.</remarks>
    private static async Task<WebApplication> StartAsync(Func<IMcpServerBuilder, IMcpServerBuilder> transport, Func<IServiceProvider, IReadOnlyList<string>>? volumes)
    {
        var url = new Uri($"http://127.0.0.1:{FreePort()}/");
        var builder = McpHttpHost.CreateBuilder([], url);

        transport(builder.Services.AddMcpServer()).WithTools<AddressTools>();

        var app = builder.Build();

        if (volumes is not null)
            app.RefuseSessionsItCannotServe(volumes);

        app.MapLocalMcp();
        await app.StartAsync();

        return app;
    }

    private static Task<McpClient> ConnectAsync(WebApplication app, string query) =>
        McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = Endpoint(app, query) }));

    private static async Task<(HttpStatusCode Status, string Said)> PostAsync(WebApplication app, string query, string? origin)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(app, query))
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", System.Text.Encoding.UTF8, "application/json"),
        };

        if (origin is not null)
            request.Headers.Add("Origin", origin);

        using var response = await http.SendAsync(request);

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static Uri Endpoint(WebApplication app, string query) => new($"{app.Urls.First().TrimEnd('/')}{McpHttpHost.DefaultPath}{query}");

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [McpServerToolType]
    private sealed class AddressTools
    {
        [McpServerTool(Name = "address", ReadOnly = true), Description("The tools the session's address names.")]
        public static string Address(ISessionAddress address) => string.Join(", ", address.Values(SessionGrant.Parameter));

        [McpServerTool(Name = "read_note", ReadOnly = true), Description("Reads the note.")]
        public static string ReadNote() => "the note";
    }
}

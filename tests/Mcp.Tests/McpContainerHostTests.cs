using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Snail.Sdk.Mcp.Http;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The host of a server in a container image: the published port on every interface, the same refusal of browsers.</summary>
public sealed class McpContainerHostTests : IAsyncLifetime
{
    private readonly int _port = FreePort();
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        var builder = McpHttpHost.CreateContainerBuilder([], _port);

        builder.Services.AddMcpServer().WithLocalHttpTransport();

        _app = builder.Build();
        _app.MapLocalMcp();

        await _app.StartAsync();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void APortNoSocketBindsTo_IsRefused(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => McpHttpHost.CreateContainerBuilder([], port));

    [Fact]
    public async Task TheServer_ListensOnTheContainerPort()
    {
        await using var client = await McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"http://127.0.0.1:{_port}{McpHttpHost.DefaultPath}") }));

        Assert.NotNull(client.ServerInfo);
        Assert.NotEmpty(client.ServerInfo.Name);
    }

    /// <summary>The refusal a server lost when it built a host of its own for the image.</summary>
    [Fact]
    public async Task ARequestFromABrowser_IsRefusedInTheContainerToo()
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"http://127.0.0.1:{_port}{McpHttpHost.DefaultPath}"))
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"ping"}""", System.Text.Encoding.UTF8, "application/json"),
        };

        request.Headers.Add("Origin", "https://example.com");

        using var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Snail.Sdk.Mcp.Http;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>A server over HTTP in the test's own process, on a free loopback port, built on the host every server is built on.</summary>
/// <remarks>
/// <para>
/// For what needs the server whole — the door, the grant read from the query, the tools Snail.Sdk adds beside the
/// server's own — without the cost of a process per test; what only a process shows, a native build and its start,
/// is <see cref="HttpServerProgram"/>'s. Snail.MCP.Git (<c>TestServer</c>) and Snail.MCP.FileSystem (<c>Listening</c>) each
/// kept a copy of these lines, and each read the port Kestrel bound in its own way.
/// </para>
/// <para>
/// The port is Kestrel's choice, port 0 on 127.0.0.1, read back once the host has started: nothing can take it between
/// the choice and the bind, as it can with <see cref="Ports.Free"/>. A server that must know its address before it
/// starts — Snail.MCP.Blender signs links with it as its services are built — is given a port, <c>Ports.Free()</c>, and
/// told its address by its settings. Disposed, the host is stopped and disposed; the sessions a test opened are the
/// test's to dispose.
/// </para>
/// </remarks>
public sealed class InProcessServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private bool _isDisposed;

    private InProcessServer(WebApplication app, Uri address, string path)
    {
        _app = app;
        Address = address;
        Endpoint = new Uri(address, path.TrimStart('/'));
    }

    /// <summary>Where the server listens: <c>http://127.0.0.1:&lt;port&gt;/</c>.</summary>
    public Uri Address { get; }

    /// <summary>The MCP endpoint of the server, <c>/mcp</c> under <see cref="Address"/> unless the start named another path.</summary>
    public Uri Endpoint { get; }

    /// <summary>The server's services: the tools it registered, its options, what a test wants to see changed.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>
    /// A host of <see cref="McpHttpHost.CreateBuilder(string[], Uri)"/> on 127.0.0.1, given its services by
    /// <paramref name="configure"/>, its endpoints by <paramref name="map"/>, and started.
    /// </summary>
    /// <param name="configure">Registers the server: <c>builder.Services.AddMcpServer().WithLocalHttpTransport().WithTools&lt;…&gt;()</c>.</param>
    /// <param name="map">Maps the endpoints; <c>app.MapLocalMcp()</c> when null.</param>
    /// <param name="path">The path <paramref name="map"/> maps the MCP endpoint at; <see cref="McpHttpHost.DefaultPath"/> unless named.</param>
    /// <param name="port">The port of 127.0.0.1 the host listens on; 0, Kestrel's choice, unless the server must know its address before it starts.</param>
    /// <param name="cancellationToken">Gives up on the start.</param>
    /// <exception cref="ArgumentOutOfRangeException">The port is negative or beyond the last one.</exception>
    public static Task<InProcessServer> StartAsync(
        Action<WebApplicationBuilder> configure,
        Action<WebApplication>? map = null,
        string path = McpHttpHost.DefaultPath,
        int port = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);

        var builder = McpHttpHost.CreateBuilder([], new Uri($"http://127.0.0.1:{port}/"));

        configure(builder);

        var app = builder.Build();

        if (map is null)
            app.MapLocalMcp(path);
        else
            map(app);

        return ServeAsync(app, path, cancellationToken);
    }

    /// <summary>A host the server built itself, as its program builds it, started and handed back with the port it bound.</summary>
    /// <example><c>await InProcessServer.ServeAsync(McpServerHost.Listening([], config, new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:0/"), false)))</c></example>
    /// <remarks>
    /// <para>
    /// For a server whose program builds its host in a method of its own — <c>McpServerHost.Listening(args, config,
    /// endpoint)</c> — given an endpoint on <c>http://127.0.0.1:0/</c>, so the test runs exactly what the program runs.
    /// The host is the server's from then on: stopped and disposed with it, and disposed too when it fails to start.
    /// </para>
    /// <para>
    /// A server that keeps a whole <c>WebApplication</c> of its own — pages, sign-in, a token in front of the MCP
    /// endpoint, <c>builder.UseMcpEndpoint(endpoint)</c> — is served alike; one that signs links with its address before
    /// it starts is given a port of its own, <c>Ports.Free()</c>, in the settings it reads the endpoint from.
    /// </para>
    /// </remarks>
    /// <param name="app">The host, built and not started, listening on a loopback address.</param>
    /// <param name="path">The path the host maps the MCP endpoint at; <see cref="McpHttpHost.DefaultPath"/> unless named.</param>
    /// <param name="cancellationToken">Gives up on the start.</param>
    public static async Task<InProcessServer> ServeAsync(WebApplication app, string path = McpHttpHost.DefaultPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);

            return new InProcessServer(app, Bound(app), path);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>A session opened at <see cref="Endpoint"/>, with <paramref name="query"/> — <c>root=…&amp;tool=…</c> — as its address when given.</summary>
    public Task<McpClient> SessionAsync(string? query = null, McpClientOptions? options = null, CancellationToken cancellationToken = default)
    {
        var endpoint = query is null ? Endpoint : new UriBuilder(Endpoint) { Query = query }.Uri;
        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint });

        return McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken);
    }

    /// <summary>Stops the host and disposes it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        try
        {
            await _app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>The address Kestrel bound, as a client reaches it: a wildcard or <c>localhost</c> is reached at 127.0.0.1.</summary>
    private static Uri Bound(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var first = addresses.FirstOrDefault() ?? throw new InvalidOperationException("the host started without an address: it was given no endpoint to listen on");
        var bound = new Uri(first.Replace("://*:", "://localhost:", StringComparison.Ordinal).Replace("://+:", "://localhost:", StringComparison.Ordinal));
        var isReachable = IPAddress.TryParse(bound.DnsSafeHost, out var address) && IPAddress.IsLoopback(address);
        var host = isReachable ? bound.Host : "127.0.0.1";

        return new Uri($"http://{host}:{bound.Port}/");
    }
}

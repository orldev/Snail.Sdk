using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.Sdk.Mcp.Http;

/// <summary>The host of a server every session of the machine shares, over Streamable HTTP on a loopback address.</summary>
/// <remarks>
/// <para>
/// Over stdio each session starts a server of its own: the runtime, and whatever the server loads, once per session.
/// Over this host every session of the machine shares one server, so they are loaded once, and a new session is
/// answered by a server already running.
/// </para>
/// <para>
/// The endpoint has no key. It listens on loopback only, and a request that carries an <c>Origin</c> is refused: a page
/// in a browser always sends one, an agent's client never does, so no web page can reach the server, not even through a
/// name rebound to 127.0.0.1.
/// </para>
/// <para>
/// The address is given to Kestrel itself, not written into the host's configuration as <c>urls</c>: the web host reads
/// <c>urls</c> from the <c>IConfiguration</c> the services hold, so a server that registered a configuration of its own
/// got Kestrel's default, localhost:5000, without a word (Snail.MCP.Documents, 2026-11-07).
/// </para>
/// </remarks>
public static class McpHttpHost
{
    /// <summary>Where clients connect unless a server says otherwise.</summary>
    public const string DefaultPath = "/mcp";

    /// <summary>Where a health check answers unless a server says otherwise.</summary>
    public const string DefaultHealthPath = "/healthz";

    private static readonly RequestDelegate Healthy = _ => Task.CompletedTask;

    /// <summary>An empty web host listening on <paramref name="url"/>, log lines on stderr at the levels its settings name, the heap trimmed when idle.</summary>
    /// <remarks><c>localhost</c> is listened on at both loopback addresses, an address at that one alone.</remarks>
    /// <exception cref="ArgumentException">The address is not a loopback one, or not plain http, which is all the host serves.</exception>
    public static WebApplicationBuilder CreateBuilder(string[] args, Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!url.IsLoopback)
            throw new ArgumentException($"'{url}' is not a loopback address: the server would answer anyone who reaches it.", nameof(url));

        if (url.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException($"'{url}' is not an http address: the host serves plain HTTP on loopback.", nameof(url));

        return Builder(args, kestrel => ListenOn(kestrel, url));
    }

    /// <summary>
    /// The same host for a server that runs in a container image: listening on <paramref name="port"/> on every interface,
    /// since a container's published port does not reach its loopback.
    /// </summary>
    /// <remarks>
    /// The boundary is the port the container publishes, which the machine running it decides; everything else is the
    /// same as <see cref="CreateBuilder(string[], Uri)"/> — the settings, stderr, the idle trim — and <c>MapLocalMcp</c> still refuses a
    /// browser's request. A server that built a host of its own for the image lost that refusal (Snail.MCP.Documents,
    /// 2026-10-16: a request with <c>Origin</c> was answered 200).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The port is not one a socket can be bound to.</exception>
    public static WebApplicationBuilder CreateContainerBuilder(string[] args, int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);

        return Builder(args, kestrel => kestrel.ListenAnyIP(port));
    }

    /// <summary>The host of the endpoint a server's settings ask for: on loopback at its URL, or on every interface at its port.</summary>
    /// <remarks>
    /// The one call for a server that reads where it answers with <see cref="McpEndpoint.Read(Microsoft.Extensions.Configuration.IConfiguration, Uri, McpTransport)"/>: inside a container, or
    /// told to listen on every interface, it is <see cref="CreateContainerBuilder"/> on the URL's port; anywhere else
    /// <see cref="CreateBuilder(string[], Uri)"/> on the URL itself, which refuses an address beyond loopback.
    /// </remarks>
    /// <exception cref="ArgumentException">The endpoint is a stdio one, or a loopback one whose address is not loopback.</exception>
    public static WebApplicationBuilder CreateBuilder(string[] args, McpEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (endpoint.Transport is not McpTransport.Http)
            throw new ArgumentException("The endpoint is a stdio one: it is served by McpHost, not by a web host.", nameof(endpoint));

        return endpoint.ListensOnEveryInterface
            ? CreateContainerBuilder(args, endpoint.Url.Port)
            : CreateBuilder(args, endpoint.Url);
    }

    private static WebApplicationBuilder Builder(string[] args, Action<KestrelServerOptions> listen)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });

        if (ServerSettings.NameOf(Assembly.GetEntryAssembly()) is { } name)
            builder.Configuration.AddServerSettings(name);

        builder.Logging.UseStderr().UseLevelsFrom(builder.Configuration);
        builder.WebHost.UseKestrelCore().ConfigureKestrel(listen);
        builder.Services.AddRoutingCore();
        builder.Services.AddIdleTrim();

        return builder;
    }

    private static void ListenOn(KestrelServerOptions kestrel, Uri url)
    {
        if (IPAddress.TryParse(url.DnsSafeHost, out var address))
            kestrel.Listen(address, url.Port);
        else
            kestrel.ListenLocalhost(url.Port);
    }

    extension(IMcpServerBuilder builder)
    {
        /// <summary>Streamable HTTP with a session for every client.</summary>
        /// <remarks>
        /// <para>
        /// Without the session the server knows nothing of the client between requests: its capabilities are null, and
        /// it cannot ask the client anything — its roots, the folder a session works in, among them.
        /// </para>
        /// <para>
        /// The 2026-07-28 revision of the protocol has no sessions, and its requests carry nothing that tells one window
        /// of a client from another. Claude Code opens with it and falls back to initialize when it is refused, so the
        /// server refuses it (-32022) rather than serving it without a session. Served that way, every window of Claude
        /// Code was one client called claude-code, and no server could learn a window's folder.
        /// </para>
        /// <para>The session's address is read from each request (<c>WithSessionAddress</c>).</para>
        /// </remarks>
        public IMcpServerBuilder WithLocalHttpTransport() =>
            builder.WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateful).WithSessionAddress();
    }

    extension(WebApplication app)
    {
        /// <summary>Maps the MCP endpoint behind the refusal of every request a browser makes.</summary>
        public IEndpointConventionBuilder MapLocalMcp(string path = DefaultPath)
        {
            app.Use(RefuseBrowsers);

            return app.MapMcp(path);
        }

        /// <summary>Maps a health check that answers 200 with no body, for a container's healthcheck and an orchestrator.</summary>
        /// <remarks>
        /// It says the process answers HTTP, nothing more: no state of the server, so it costs nothing and tells a caller
        /// nothing it could use. A request that carries an <c>Origin</c> is refused here too when the endpoint is mapped
        /// with <c>MapLocalMcp</c>, whose refusal is the whole host's; a healthcheck — <c>curl</c>, <c>wget</c>, Docker's —
        /// sends none.
        /// </remarks>
        public IEndpointConventionBuilder MapHealth(string path = DefaultHealthPath) => app.MapGet(path, Healthy);
    }

    internal static Task RefuseBrowsers(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Headers.Origin.Any())
            return next(context);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    }
}

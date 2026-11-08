using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
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
/// <para>
/// The server's settings are found by the entry assembly, as <c>McpHost.CreateBuilder</c> finds them: a host built in a
/// test's own process has the test runner for its entry assembly and reads none. Such a test — and a program that read
/// its settings already, to choose its endpoint — passes them to <see cref="CreateBuilder(string[], McpEndpoint, IConfiguration)"/>.
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

        CheckLoopback(url, nameof(url));

        return Builder(args, kestrel => ListenOn(kestrel, url));
    }

    private static void CheckLoopback(Uri url, string parameter)
    {
        if (!url.IsLoopback)
            throw new ArgumentException($"'{url}' is not a loopback address: the server would answer anyone who reaches it.", parameter);

        if (url.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException($"'{url}' is not an http address: the host serves plain HTTP on loopback.", parameter);
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
    /// The one call for a server that reads where it answers with <see cref="McpEndpoint.Read(IConfiguration, Uri)"/> or <see cref="McpEndpoint.ReadHttp(IConfiguration, Uri)"/>: inside a container, or
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

    /// <summary>The host of the endpoint a server's settings ask for, over those settings as the program read them rather than read again.</summary>
    /// <param name="args">The program's arguments.</param>
    /// <param name="endpoint">Where the server answers, as <see cref="McpEndpoint.Read(IConfiguration, Uri)"/> read it from <paramref name="settings"/>.</param>
    /// <param name="settings">The server's settings as the program holds them, with whatever the server adds to them, which become the host's configuration.</param>
    /// <remarks>
    /// <para>
    /// <see cref="CreateBuilder(string[], McpEndpoint)"/>, but the host's configuration is <paramref name="settings"/>: the
    /// program read them to choose its endpoint, and the host read the same files and variables a second time. A server
    /// whose configuration holds more than <see cref="ServerSettings"/> reads — the browser defaults of
    /// Snail.MCP.Playwright — then added its own on top. Nothing is looked for by the entry assembly, so a test that
    /// builds the host in its own process, under the test runner, gives it the settings the server would have read.
    /// </para>
    /// <para>
    /// The folders are not moved from the snail names here: reading the settings moved them
    /// (<see cref="ServerSettings.Read(string)"/>, <c>AddServerSettings(name)</c>).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The endpoint is a stdio one, or a loopback one whose address is not loopback or not plain http.</exception>
    public static WebApplicationBuilder CreateBuilder(string[] args, McpEndpoint endpoint, IConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(settings);

        if (endpoint.Transport is not McpTransport.Http)
            throw new ArgumentException("The endpoint is a stdio one: it is served by McpHost, not by a web host.", nameof(endpoint));

        if (endpoint.ListensOnEveryInterface)
            return Builder(args, kestrel => kestrel.ListenAnyIP(endpoint.Url.Port), settings);

        CheckLoopback(endpoint.Url, nameof(endpoint));

        return Builder(args, kestrel => ListenOn(kestrel, endpoint.Url), settings);
    }

    private static WebApplicationBuilder Builder(string[] args, Action<KestrelServerOptions> listen, IConfiguration? settings = null)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });

        if (settings is not null)
            builder.Configuration.AddConfiguration(settings);
        else if (ServerSettings.NameOf(Assembly.GetEntryAssembly()) is { } name)
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

        /// <summary>Maps a health check for a container's healthcheck and an orchestrator: 200 with no body, or 503 while <paramref name="isHealthy"/> says the server cannot serve.</summary>
        /// <param name="path">Where the check answers.</param>
        /// <param name="isHealthy">Whether the server can serve now, asked with the request's services and the token cancelled when the caller hangs up; without it the check says only that the process answers HTTP.</param>
        /// <remarks>
        /// <para>
        /// Without <paramref name="isHealthy"/> it says the process answers HTTP, nothing more: no state of the server, so it
        /// costs nothing and tells a caller nothing it could use. A server whose process may answer HTTP while what it serves
        /// through cannot gives the check that asks it: Snail.MCP.Playwright answers nothing useful while its browser does
        /// not, so an orchestrator should restart it, and it kept a <c>/healthz</c> of its own for that. The check is asked
        /// at every request, so it should be cheap, or remember its last answer. A check that throws answers 503, as one that
        /// says no, rather than 500: to a healthcheck both mean the server is not ready; a request its caller abandoned stays
        /// abandoned.
        /// </para>
        /// <para>
        /// A request that carries an <c>Origin</c> is refused here too when the endpoint is mapped with <c>MapLocalMcp</c>,
        /// whose refusal is the whole host's; a healthcheck — <c>curl</c>, <c>wget</c>, Docker's — sends none.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">The path is empty or blank.</exception>
        public IEndpointConventionBuilder MapHealth(string path = DefaultHealthPath, Func<IServiceProvider, CancellationToken, Task<bool>>? isHealthy = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            return isHealthy is null
                ? app.MapGet(path, Healthy)
                : app.MapGet(path, context => AnswerHealthAsync(context, isHealthy));
        }
    }

    private static async Task AnswerHealthAsync(HttpContext context, Func<IServiceProvider, CancellationToken, Task<bool>> isHealthy)
    {
        var isReady = await IsReadyAsync(context, isHealthy);

        context.Response.StatusCode = isReady ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
    }

    private static async Task<bool> IsReadyAsync(HttpContext context, Func<IServiceProvider, CancellationToken, Task<bool>> isHealthy)
    {
        try
        {
            return await isHealthy(context.RequestServices, context.RequestAborted);
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            return false;
        }
    }

    internal static Task RefuseBrowsers(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Headers.Origin.Any())
            return next(context);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    }
}

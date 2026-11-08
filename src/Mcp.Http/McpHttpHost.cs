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
/// The endpoint has no key. It listens on loopback only, and a request to it that carries an <c>Origin</c> is refused: a
/// page in a browser always sends one, an agent's client never does, so no web page can reach the server, not even
/// through a name rebound to 127.0.0.1. A server that reads its endpoint holds it to loopback with
/// <see cref="McpReach"/>; the read refuses what would open it beyond.
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

    private static readonly HealthAnswer Unhealthy = new(IsHealthy: false);

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

    /// <summary>The host of the endpoint a server's settings ask for: at its URL's address, or on every interface at its port.</summary>
    /// <remarks>
    /// <para>
    /// The one call for a server that reads where it answers with <see cref="McpEndpoint.Read(IConfiguration, Uri)"/> or
    /// <see cref="McpEndpoint.ReadHttp(IConfiguration, Uri)"/>: inside a container, or told to listen on every interface,
    /// it listens on the URL's port of every interface, as <see cref="CreateContainerBuilder"/> does; anywhere else on the
    /// URL's address — loopback, or an IP address beyond it, a LAN one, that the read let through.
    /// </para>
    /// <para>
    /// How far the endpoint may reach is the read's to decide, where a refusal names the variable the operator set
    /// (<see cref="McpReach"/>): this host refused an address beyond loopback with an <see cref="ArgumentException"/>
    /// that named none, and could not listen on a LAN address that a server guarded by a token asked for.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The endpoint is a stdio one, or its URL is not a plain http one at an IP address or localhost.</exception>
    public static WebApplicationBuilder CreateBuilder(string[] args, McpEndpoint endpoint)
    {
        var listen = ListenerOf(endpoint);

        return Builder(args, listen);
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
    /// <exception cref="ArgumentException">The endpoint is a stdio one, or its URL is not a plain http one at an IP address or localhost.</exception>
    public static WebApplicationBuilder CreateBuilder(string[] args, McpEndpoint endpoint, IConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var listen = ListenerOf(endpoint);

        return Builder(args, listen, settings);
    }

    /// <summary>The host of a stdio server that serves HTTP of its own beside MCP: an empty web host over its settings, log lines on stderr at the levels they name, the heap trimmed when idle, and no MCP endpoint.</summary>
    /// <param name="args">The program's arguments.</param>
    /// <param name="settings">The server's settings as the program holds them, which become the host's configuration.</param>
    /// <remarks>
    /// <para>
    /// <c>McpHost.CreateBuilder(args, settings)</c> on a web builder: Kestrel with routing, listening only where the server
    /// tells it (<c>builder.WebHost.ConfigureKestrel(kestrel =&gt; kestrel.ListenLocalhost(port))</c>), since the MCP
    /// endpoint is stdio's. Snail.MCP.Figma's bridge to its plugin is a Kestrel endpoint of the stdio server's process,
    /// and its <c>CreateStdioBuilder</c> repeated these lines; a stdio server gets no browser refusal here, as its own
    /// endpoints are its to guard.
    /// </para>
    /// <para>
    /// A host given no address listens on Kestrel's default, <c>localhost:5000</c>: the server names one.
    /// </para>
    /// </remarks>
    public static WebApplicationBuilder CreateStdioBuilder(string[] args, IConfiguration settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Builder(args, static _ => { }, settings);
    }

    private static WebApplicationBuilder Builder(string[] args, Action<KestrelServerOptions> listen, IConfiguration? settings = null)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });

        if (settings is not null)
            builder.Configuration.AddConfiguration(settings);
        else if (ServerSettings.NameOf(Assembly.GetEntryAssembly()) is { } name)
            builder.Configuration.AddServerSettings(name);

        builder.WebHost.UseKestrelCore();
        builder.Services.AddRoutingCore();
        ServeAt(builder, listen);

        return builder;
    }

    /// <summary>Kestrel listening as <paramref name="listen"/> says, log lines on stderr at the levels the configuration names, the heap trimmed when idle.</summary>
    private static void ServeAt(WebApplicationBuilder builder, Action<KestrelServerOptions> listen)
    {
        builder.Logging.UseStderr().UseLevelsFrom(builder.Configuration);
        builder.WebHost.ConfigureKestrel(listen);
        builder.Services.AddIdleTrim();
    }

    /// <summary>Where Kestrel listens for the endpoint: every interface at its port, or its address.</summary>
    private static Action<KestrelServerOptions> ListenerOf(McpEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (endpoint.Transport is not McpTransport.Http)
            throw new ArgumentException("The endpoint is a stdio one: it is served by McpHost, not by a web host.", nameof(endpoint));

        var url = endpoint.Url;

        if (url.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException($"'{url}' is not an http address: the host serves plain HTTP.", nameof(endpoint));

        if (endpoint.ListensOnEveryInterface)
            return kestrel => kestrel.ListenAnyIP(url.Port);

        var isAddress = IPAddress.TryParse(url.DnsSafeHost, out _) || string.Equals(url.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase);

        if (!isAddress)
            throw new ArgumentException($"'{url}' names a host, not an address: the host listens on an IP address or localhost.", nameof(endpoint));

        return kestrel => ListenOn(kestrel, url);
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

    extension(WebApplicationBuilder builder)
    {
        /// <summary>Makes a web builder the server made itself listen where the endpoint says, log on stderr at the levels its configuration names, and trim the heap when idle, as the builders of this host do.</summary>
        /// <param name="endpoint">Where the server answers, as <see cref="McpEndpoint.Read(IConfiguration, Uri)"/> read it.</param>
        /// <remarks>
        /// <para>
        /// For a server that keeps a whole <see cref="WebApplication.CreateBuilder(string[])"/> — Razor pages, sign-in,
        /// antiforgery, which the empty builders of this host leave out — behind a key of its own: Snail.MCP.Blender
        /// cleared <c>app.Urls</c> and wrote the address there by hand, with the stderr logging, the levels and the idle
        /// trim missing. The address is given to Kestrel itself, as the builders of this host give it, so a <c>urls</c>
        /// setting or <c>ASPNETCORE_URLS</c> does not move it.
        /// </para>
        /// <para>
        /// The builder's configuration is the server's to fill: <c>builder.Configuration.AddConfiguration(settings)</c>
        /// before this call, so the levels it names are read. The browser refusal is <c>MapLocalMcp</c>'s or
        /// <c>UseBrowserRefusal</c>'s, as on any host.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">The endpoint is a stdio one, or its URL is not a plain http one at an IP address or localhost.</exception>
        public WebApplicationBuilder UseMcpEndpoint(McpEndpoint endpoint)
        {
            var listen = ListenerOf(endpoint);

            ServeAt(builder, listen);

            return builder;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>Maps the MCP endpoint behind the refusal of every request to it that a browser makes.</summary>
        /// <remarks>
        /// <para>
        /// The refusal holds for <paramref name="path"/> and the paths under it (<see cref="UseBrowserRefusal"/>), not for
        /// the whole host: Snail.MCP.Figma's plugin calls the bridge of the same process with <c>Origin: null</c>, and the
        /// pages of Snail.MCP.AfterEffects post their forms with an <c>Origin</c>, so each mapped the endpoint itself
        /// behind a refusal of its own. The endpoints a server maps beside it are its own to guard.
        /// </para>
        /// <para>
        /// The refusal joins the pipeline where this is called: a server that answers a request with authentication
        /// first calls <see cref="UseBrowserRefusal"/> before it, so a browser is refused before it is challenged.
        /// </para>
        /// </remarks>
        public IEndpointConventionBuilder MapLocalMcp(string path = DefaultPath)
        {
            app.UseBrowserRefusal(path);

            return app.MapMcp(path);
        }

        /// <summary>Refuses with 403 a request to <paramref name="path"/>, or a path under it, that carries an <c>Origin</c>, as a page in a browser does and an agent's client never does.</summary>
        /// <param name="path">The MCP endpoint's path; <see cref="DefaultPath"/> unless named.</param>
        /// <param name="port">The local port the refusal holds on; every port when null.</param>
        /// <remarks>
        /// <para>
        /// For a server that maps the MCP endpoint itself — behind its own authorization, on one port of several — and
        /// keeps the refusal <see cref="MapLocalMcp"/> holds. The path is matched by whole segments and without regard to
        /// case, as routing matches it: <c>/mcp</c> and <c>/MCP/sse</c>, not <c>/mcpx</c>. With a port, a request that
        /// reached another port of the host — Snail.MCP.Figma's bridge — passes.
        /// </para>
        /// <para>The refusal joins the pipeline where this is called, so it comes before the authentication a server adds after it.</para>
        /// </remarks>
        /// <exception cref="ArgumentException">The path is empty, blank, or does not start with <c>/</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The port is not one a socket can be bound to.</exception>
        public WebApplication UseBrowserRefusal(string path = DefaultPath, int? port = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (!path.StartsWith('/'))
                throw new ArgumentException($"'{path}' is not a path: it starts with '/'.", nameof(path));

            if (port is { } bound)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bound);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(bound, ushort.MaxValue);
            }

            var refused = new PathString(path.TrimEnd('/'));

            app.Use((context, next) => IsRefused(context, refused, port) ? RefuseAsync(context) : next(context));

            return app;
        }

        /// <summary>Maps a health check for a container's healthcheck and an orchestrator: 200, or 503 while <paramref name="check"/> says the server cannot serve, with the body the check answers.</summary>
        /// <param name="path">Where the check answers; <see cref="DefaultHealthPath"/>, the one a container's healthcheck and <c>HttpServerProgram</c> ask, unless named.</param>
        /// <param name="check">Whether the server can serve now, and what the check says about it, asked with the request's services and the token cancelled when the caller hangs up; without it the check says only that the process answers HTTP.</param>
        /// <remarks>
        /// <para>
        /// Without <paramref name="check"/> it answers 200 with no body: the process answers HTTP, nothing more, so it costs
        /// nothing and tells a caller nothing it could use. A server whose process may answer HTTP while what it serves
        /// through cannot gives the check that asks it: Snail.MCP.Playwright answers nothing useful while its browser does
        /// not, so an orchestrator should restart it — <c>async (services, cancellationToken) =&gt; new
        /// HealthAnswer(await IsBrowserAnsweringAsync(services, cancellationToken))</c>.
        /// </para>
        /// <para>
        /// A check whose answer is a contract with a body gives the body: Snail.MCP.Blender's <c>/healthz</c> answers
        /// <c>{"status":"ok","blender":"up"}</c> — or <c>down</c>, or <c>stalled</c> — and AfterEffects' says the same of
        /// After Effects, so each mapped its check by hand. A server that stays healthy while what it drives is down, as
        /// those two do, answers healthy with the body that says so; one an orchestrator should restart answers unhealthy.
        /// The body is written as <c>application/json</c>.
        /// </para>
        /// <para>
        /// The check is asked at every request, so it should be cheap, or remember its last answer. A check that throws
        /// answers 503 with no body, as one that says no, rather than 500: to a healthcheck both mean the server is not
        /// ready; a request its caller abandoned stays abandoned. The check is not behind the browser refusal, which holds
        /// for the MCP endpoint alone; a healthcheck — <c>curl</c>, <c>wget</c>, Docker's — sends no <c>Origin</c> anyway.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">The path is empty or blank.</exception>
        public IEndpointConventionBuilder MapHealth(string path = DefaultHealthPath, Func<IServiceProvider, CancellationToken, Task<HealthAnswer>>? check = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (check is null)
                return app.MapGet(path, Healthy);

            return app.MapGet(path, context => AnswerHealthAsync(context, check));
        }
    }

    private static async Task AnswerHealthAsync(HttpContext context, Func<IServiceProvider, CancellationToken, Task<HealthAnswer>> check)
    {
        var answer = await AskAsync(context, check);

        context.Response.StatusCode = answer.IsHealthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;

        if (answer.Body is null)
            return;

        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(answer.Body.ToJsonString(), context.RequestAborted);
    }

    private static async Task<HealthAnswer> AskAsync(HttpContext context, Func<IServiceProvider, CancellationToken, Task<HealthAnswer>> check)
    {
        try
        {
            return await check(context.RequestServices, context.RequestAborted) ?? Unhealthy;
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            return Unhealthy;
        }
    }

    private static bool IsRefused(HttpContext context, PathString refused, int? port)
    {
        var isBrowser = context.Request.Headers.Origin.Count > 0;
        var isEndpoint = context.Request.Path.StartsWithSegments(refused, StringComparison.OrdinalIgnoreCase);
        var isPort = port is null || context.Connection.LocalPort == port;

        return isBrowser && isEndpoint && isPort;
    }

    private static Task RefuseAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    }
}

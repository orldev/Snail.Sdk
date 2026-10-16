using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// </remarks>
public static class McpHttpHost
{
    /// <summary>Where clients connect unless a server says otherwise.</summary>
    public const string DefaultPath = "/mcp";

    /// <summary>An empty web host listening on <paramref name="url"/>, log lines on stderr, a request a line only on warnings, the heap trimmed when idle.</summary>
    /// <exception cref="ArgumentException">The address is not a loopback one.</exception>
    public static WebApplicationBuilder CreateBuilder(string[] args, Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (!url.IsLoopback)
            throw new ArgumentException($"'{url}' is not a loopback address: the server would answer anyone who reaches it.", nameof(url));

        return Builder(args, url.ToString());
    }

    /// <summary>
    /// The same host for a server that runs in a container image: listening on <paramref name="port"/> on every interface,
    /// since a container's published port does not reach its loopback.
    /// </summary>
    /// <remarks>
    /// The boundary is the port the container publishes, which the machine running it decides; everything else is the
    /// same as <see cref="CreateBuilder"/> — the settings, stderr, the idle trim — and <c>MapLocalMcp</c> still refuses a
    /// browser's request. A server that built a host of its own for the image lost that refusal (Snail.MCP.Documents,
    /// 2026-10-16: a request with <c>Origin</c> was answered 200).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The port is not one a socket can be bound to.</exception>
    public static WebApplicationBuilder CreateContainerBuilder(string[] args, int port)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);

        return Builder(args, $"http://*:{port}");
    }

    private static WebApplicationBuilder Builder(string[] args, string url)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions { Args = args });

        if (ServerSettings.NameOf(Assembly.GetEntryAssembly()) is { } name)
            builder.Configuration.AddServerSettings(name);

        builder.Logging.UseStderr().AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.WebHost.UseKestrelCore().UseUrls(url);
        builder.Services.AddRoutingCore();
        builder.Services.AddIdleTrim();

        return builder;
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
        /// </remarks>
        public IMcpServerBuilder WithLocalHttpTransport() =>
            builder.WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateful);
    }

    extension(WebApplication app)
    {
        /// <summary>Maps the MCP endpoint behind the refusal of every request a browser makes.</summary>
        public IEndpointConventionBuilder MapLocalMcp(string path = DefaultPath)
        {
            app.Use(RefuseBrowsers);

            return app.MapMcp(path);
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

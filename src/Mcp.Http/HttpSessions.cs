using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Http;

/// <summary>The address a session was opened at, read from the request being served, and the sessions turned away at the door.</summary>
public static partial class HttpSessions
{
    /// <summary>The header Streamable HTTP carries a session's id in, from the response to <c>initialize</c> on.</summary>
    public const string Header = "Mcp-Session-Id";

    extension(IMcpServerBuilder builder)
    {
        /// <summary>The session's address — <c>?root=</c>, <c>?tool=</c> — read from the query of the request being served.</summary>
        /// <remarks>
        /// <c>WithLocalHttpTransport</c> registers it by itself; a server on the MCP SDK's default transport, which keeps no
        /// sessions, calls this beside <c>WithHttpTransport</c>. Every request of a session is posted to the same URL, so the
        /// query of the current one answers about the session; outside a request — a job that outlived its call — it
        /// answers nothing. Called before or after <c>WithSessionGrant</c> and <c>WithSessionRoots</c> alike: it
        /// replaces the address they register, and they add theirs only where there is none. Its
        /// <see cref="ISessionAddress.Subject"/> is <c>request</c> on a transport that keeps no sessions
        /// (<c>HttpServerSessionMode.Stateless</c>), so a refusal there speaks of the request rather than of a session.
        /// </remarks>
        public IMcpServerBuilder WithSessionAddress()
        {
            builder.Services.AddHttpContextAccessor();
            builder.Services.RemoveAll<ISessionAddress>();
            builder.Services.AddSingleton<ISessionAddress>(provider => new HttpSessionAddress(provider.GetRequiredService<IHttpContextAccessor>(), provider));

            return builder;
        }
    }

    extension(JobOwners)
    {
        /// <summary>The owner of a request: the MCP session it belongs to, or none outside a session.</summary>
        /// <remarks>
        /// <para>
        /// For a server that holds no task of a caller — Snail.MCP.Sandbox — where the session is the one identity it has,
        /// and the session that started a run is the one that awaits it: <c>.WithJobs("&lt;short&gt;", JobOwners.Session)</c>,
        /// and a tool starts its job with <c>JobOwners.Session(services)</c>. Each server read the session's header itself,
        /// since the MCP SDK keeps its name internal.
        /// </para>
        /// <para>
        /// Read from the <see cref="Header"/> of the request being served, which the MCP SDK checks against a
        /// session it opened before any handler runs, so a client cannot claim another's. A transport that keeps no
        /// sessions (<c>HttpServerSessionMode.Stateless</c>) leaves the header unchecked, so there it names nobody, as it
        /// does over stdio: such jobs belong to whoever holds their URI. It needs the request: <c>WithLocalHttpTransport</c>
        /// or <c>WithSessionAddress</c> registers what reads it, and without either it is refused with that said rather
        /// than leave every job to whoever holds its URI.
        /// </para>
        /// </remarks>
        /// <param name="services">The services the request is served with.</param>
        public static string? Session(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            var requests = services.GetService<IHttpContextAccessor>()
                ?? throw new InvalidOperationException("JobOwners.Session reads the request being served, and no IHttpContextAccessor is registered: call WithLocalHttpTransport or WithSessionAddress.");

            if (IsStateless(services))
                return null;

            return requests.HttpContext?.Request.Headers[Header] is { Count: > 0 } session && !string.IsNullOrWhiteSpace(session) ? session.ToString() : null;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// Answers 400, with the reason, to a request at <paramref name="path"/> whose session names a root this machine does
        /// not have or the <paramref name="volumes"/> do not hold, or names none where no volume is configured.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Before a session exists to serve it. A root missing here means discovery handed the worker of one machine the
        /// server of another: served, the session would hear about folders that are not its copy, and nothing in an answer
        /// would show it. A root outside the volumes is not a working copy, whoever wrote the address. A session naming no
        /// root at a server configured with none would be served nothing, and hears so once rather than from every call.
        /// </para>
        /// <para>
        /// A request with an <c>Origin</c> is refused 403 first, as <c>MapLocalMcp</c> refuses it, whichever of the two is
        /// called first: a 400 that named a missing folder would tell a page in a browser which copies the machine has.
        /// </para>
        /// <para>
        /// <see cref="SessionRootsOptions.ReachesEverywhereAsAProgram"/> opens nothing here: it is for a program without an
        /// address, and a session over HTTP that names no root at a server with no volume is turned away as before — the
        /// door refuses what <see cref="SessionRoots.Reach"/> would refuse on every call.
        /// </para>
        /// </remarks>
        /// <param name="volumes">The folders the server serves, as given to <c>WithSessionRoots</c>.</param>
        /// <param name="path">The MCP endpoint; any other path is let through.</param>
        public WebApplication UseSessionRoots(Func<IServiceProvider, IReadOnlyList<string>> volumes, string path = McpHttpHost.DefaultPath)
        {
            ArgumentNullException.ThrowIfNull(volumes);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            PathString door = new(path.TrimEnd('/'));

            app.Use((context, next) => DoorAsync(app, volumes, door, context, next));

            return app;
        }
    }

    /// <remarks>
    /// The path is compared as routing compares it, ignoring case: compared ordinally, <c>POST /MCP?root=/</c> passed the
    /// door and reached <c>MapMcp</c>, which answers <c>/MCP</c> as it answers <c>/mcp</c>.
    /// </remarks>
    private static async Task DoorAsync(WebApplication app, Func<IServiceProvider, IReadOnlyList<string>> volumes, PathString path, HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (context.Request.Headers.Origin.Any())
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var configured = volumes(app.Services);
        var options = app.Services.GetService<SessionRootsOptions>();
        var roots = new SessionRoots(new RequestAddress(context.Request), configured, options);

        if (Refusal(roots, configured, path.Value ?? string.Empty, options, SubjectOf(context.RequestServices)) is not { } refusal)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        TurnedAway(app.Logger, refusal);

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "text/plain; charset=utf-8";

        await context.Response.WriteAsync(refusal, context.RequestAborted).ConfigureAwait(false);
    }

    /// <summary>Why the session may not be opened, in the fleet's words, or null when it may.</summary>
    internal static string? Refusal(SessionRoots roots, IReadOnlyList<string> volumes, string path, SessionRootsOptions? options = null, string subject = "session")
    {
        IReadOnlyList<string> configured = [.. volumes.Where(volume => !string.IsNullOrWhiteSpace(volume))];

        if (roots.Named.Count == 0 && configured.Count == 0)
        {
            string orSet = options?.RootsSetting is { Length: > 0 } setting ? $", or set {setting}" : string.Empty;

            return $"this {subject} named no root and this server is configured with none, so it can serve nothing: name the copy in the address, {path}?{SessionRoots.Parameter}=/path/to/the/copy{orSet}";
        }

        if (roots.Unreadable is { Count: > 0 } unreadable)
            return $"this server may not read {string.Join(", ", unreadable)}: the disk refused it or did not answer — a folder this server may not enter, a share that timed out; ask whoever runs the server";

        if (roots.Absent is { Count: > 0 } absent)
            return $"this machine has no {string.Join(", ", absent)}: the {subject} named a working copy that is not on the host this server runs on";

        if (roots.Outside is not { Count: > 0 } outside)
            return null;

        return configured.Count == 0
            ? $"this server is configured with no root, so it serves none a {subject} names, {string.Join(", ", outside)} among them: {(options?.RootsSetting is { Length: > 0 } rootsSetting ? $"set {rootsSetting}" : "configure the server's roots")}"
            : $"this server serves the working copies under {string.Join(", ", configured)}, and {string.Join(", ", outside)} is not one of them";
    }

    /// <summary>Whether the transport keeps no sessions, so that each request stands alone and no session header is checked.</summary>
    internal static bool IsStateless(IServiceProvider services) =>
        services.GetService<IOptions<HttpServerTransportOptions>>()?.Value.SessionMode == HttpServerSessionMode.Stateless;

    /// <summary>What a refusal calls the one it speaks to on this transport.</summary>
    internal static string SubjectOf(IServiceProvider? services) => services is not null && IsStateless(services) ? "request" : "session";

    [LoggerMessage(Level = LogLevel.Warning, Message = "A session was turned away: {Reason}")]
    private static partial void TurnedAway(ILogger logger, string reason);
}

/// <summary>The address of the request being served, which every request of a session is posted to.</summary>
/// <remarks>Always present: a server over HTTP is reached at an address, and a session that names nothing there named nothing.</remarks>
internal sealed class HttpSessionAddress(IHttpContextAccessor requests, IServiceProvider services) : ISessionAddress
{
    public string Subject => HttpSessions.SubjectOf(services);

    public IReadOnlyList<string> Values(string parameter) =>
        requests.HttpContext is { } context ? new RequestAddress(context.Request).Values(parameter) : [];
}

/// <summary>The address of one request: the values of its query, blanks left out.</summary>
internal sealed class RequestAddress(HttpRequest request) : ISessionAddress
{
    public string Subject => HttpSessions.SubjectOf(request.HttpContext.RequestServices);

    public IReadOnlyList<string> Values(string parameter) =>
        [.. request.Query[parameter].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)];
}

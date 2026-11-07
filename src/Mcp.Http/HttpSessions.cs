using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Http;

/// <summary>The address a session was opened at, read from the request being served, and the sessions turned away at the door.</summary>
public static partial class HttpSessions
{
    extension(IMcpServerBuilder builder)
    {
        /// <summary>The session's address — <c>?root=</c>, <c>?tool=</c> — read from the query of the request being served.</summary>
        /// <remarks>
        /// <c>WithLocalHttpTransport</c> registers it by itself; a server on the MCP SDK's default transport, which keeps no
        /// sessions, calls this beside <c>WithHttpTransport</c>. Every request of a session is posted to the same URL, so the
        /// query of the current one answers about the session; outside a request — a job that outlived its call — it
        /// answers nothing.
        /// </remarks>
        public IMcpServerBuilder WithSessionAddress()
        {
            builder.Services.AddHttpContextAccessor();
            builder.Services.RemoveAll<ISessionAddress>();
            builder.Services.AddSingleton<ISessionAddress>(provider => new HttpSessionAddress(provider.GetRequiredService<IHttpContextAccessor>()));

            return builder;
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
        /// </remarks>
        /// <param name="volumes">The folders the server serves, as given to <c>WithSessionRoots</c>.</param>
        /// <param name="path">The MCP endpoint; any other path is let through.</param>
        public WebApplication RefuseSessionsItCannotServe(Func<IServiceProvider, IReadOnlyList<string>> volumes, string path = McpHttpHost.DefaultPath)
        {
            ArgumentNullException.ThrowIfNull(volumes);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            app.Use((context, next) => DoorAsync(app, volumes, path, context, next));

            return app;
        }
    }

    private static async Task DoorAsync(WebApplication app, Func<IServiceProvider, IReadOnlyList<string>> volumes, string path, HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments(path, StringComparison.Ordinal))
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
        var roots = new SessionRoots(new RequestAddress(context.Request), configured);

        if (Refusal(roots, configured, path) is not { } refusal)
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
    internal static string? Refusal(SessionRoots roots, IReadOnlyList<string> volumes, string path)
    {
        IReadOnlyList<string> configured = [.. volumes.Where(volume => !string.IsNullOrWhiteSpace(volume))];

        if (roots.Named.Count == 0 && configured.Count == 0)
            return $"this session named no root and this server is configured with none, so it can serve nothing: open the session at {path}?{SessionRoots.Parameter}=/path/to/the/copy";

        if (roots.Absent is { Count: > 0 } absent)
            return $"this machine has no {string.Join(", ", absent)}: the session named a working copy that is not on the host this server runs on";

        return roots.Outside is { Count: > 0 } outside
            ? $"this server serves the working copies under {string.Join(", ", configured)}, and {string.Join(", ", outside)} is not one of them"
            : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A session was turned away: {Reason}")]
    private static partial void TurnedAway(ILogger logger, string reason);
}

/// <summary>The address of the request being served, which every request of a session is posted to.</summary>
/// <remarks>Always present: a server over HTTP is reached at an address, and a session that names nothing there named nothing.</remarks>
internal sealed class HttpSessionAddress(IHttpContextAccessor requests) : ISessionAddress
{
    public IReadOnlyList<string> Values(string parameter) =>
        requests.HttpContext is { } context ? new RequestAddress(context.Request).Values(parameter) : [];
}

/// <summary>The address of one request: the values of its query, blanks left out.</summary>
internal sealed class RequestAddress(HttpRequest request) : ISessionAddress
{
    public IReadOnlyList<string> Values(string parameter) =>
        [.. request.Query[parameter].Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)];
}

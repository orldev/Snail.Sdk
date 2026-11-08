using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>The server's tool collection, able to say that the lists made from it changed although no tool came or went.</summary>
/// <remarks>
/// Every session's server listens to its collection and tells its client of a change on the session's stream; the
/// listen streams of the clients of the 2026-07-28 revision hear it through <c>ListenStreams</c>, which listens to it
/// too. An expiry happens outside any request, so this is the one way to reach a session then; the sessions that lost
/// nothing read an unchanged list once more.
/// </remarks>
internal sealed class SkillToolCollection : McpServerPrimitiveCollection<McpServerTool>
{
    /// <summary>A collection holding the tools of <paramref name="tools"/>.</summary>
    public SkillToolCollection(IEnumerable<McpServerTool> tools)
    {
        foreach (var tool in tools)
            TryAdd(tool);
    }

    /// <summary>Tells every session that its tool list changed.</summary>
    public void Announce() => RaiseChanged();
}

/// <summary>The one tool collection every options instance of the host is given once skills are on, so a change announced reaches every session.</summary>
/// <remarks>
/// Over HTTP the MCP SDK builds the options anew for each session and, to a sessionless client, for each request — its
/// tool collection with them. A change announced through one of them reached the sessions that listened to that one:
/// none, when an expiry announced through the host's own options, and none either when a sessionless client's request
/// did. The tools are the same singletons in each, so one collection holds them for all.
/// </remarks>
internal sealed class SharedToolCollection
{
    private readonly Lock _gate = new();

    private SkillToolCollection? _shared;

    /// <summary>The shared collection, made from the first collection the options were filled with.</summary>
    public SkillToolCollection Of(McpServerPrimitiveCollection<McpServerTool>? filled)
    {
        lock (_gate)
            return _shared ??= new SkillToolCollection(filled ?? []);
    }
}

/// <summary>Unloads the skills a session has not used for <see cref="SkillOptions.IdleTimeout"/>, and tells the sessions their list changed.</summary>
/// <remarks>A long session otherwise keeps every tool it ever loaded in the model's context. Nothing runs when the timeout is zero.</remarks>
internal sealed partial class SkillExpiry(SkillSessions sessions, SkillOptions options, IOptions<McpServerOptions> server, ILogger<SkillExpiry>? logger = null) : BackgroundService
{
    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>How often the sessions are looked at: a quarter of the timeout, between a second and a minute.</summary>
    internal TimeSpan Period => TimeSpan.FromTicks(Math.Clamp(options.IdleTimeout.Ticks / 4, TimeSpan.TicksPerSecond, TimeSpan.TicksPerMinute));

    /// <summary>Unloads what has expired in every session and, when anything was, tells the sessions.</summary>
    internal void Sweep()
    {
        var expired = sessions.Expire(options.IdleTimeout);

        if (expired.Count == 0)
            return;

        Unloaded(_logger, string.Join(", ", expired));
        (server.Value.ToolCollection as SkillToolCollection)?.Announce();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.IdleTimeout <= TimeSpan.Zero)
            return;

        using var timer = new PeriodicTimer(Period, sessions.Time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                Sweep();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Unloaded idle skills: {Skills}")]
    private static partial void Unloaded(ILogger logger, string skills);
}

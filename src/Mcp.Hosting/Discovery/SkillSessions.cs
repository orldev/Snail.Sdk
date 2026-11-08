using System.Collections.Concurrent;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>A skill as the server declares it: its name, what it is for, and the tools it loads.</summary>
internal sealed record Skill(string Name, string Description, IReadOnlyList<string> Tools)
{
    /// <summary>The skills the tools declare, by name, each with its tools in the order given.</summary>
    public static IReadOnlyList<Skill> Of(IEnumerable<McpServerTool> tools)
    {
        var declared = tools
            .Select(tool => (Tool: tool.ProtocolTool.Name, Skill: tool.Metadata.OfType<SkillAttribute>().FirstOrDefault()))
            .Where(each => each.Skill is not null)
            .GroupBy(each => each.Skill!.Name, StringComparer.OrdinalIgnoreCase);
        var skills = declared.Select(Grouped);

        return [.. skills.OrderBy(skill => skill.Name, StringComparer.Ordinal)];
    }

    private static Skill Grouped(IGrouping<string, (string Tool, SkillAttribute? Skill)> group)
    {
        var first = group.First().Skill!;

        return new Skill(first.Name, first.Description, [.. group.Select(each => each.Tool)]);
    }
}

/// <summary>The skills one session has loaded, and when it last called a tool of each.</summary>
internal sealed class LoadedSkills
{
    private readonly Dictionary<string, DateTimeOffset> _used = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate = new();

    private DateTimeOffset _seen;

    /// <summary>Whether nothing is loaded and the session has not been heard from for <paramref name="idle"/>, so the entry says nothing any more.</summary>
    public bool IsForgotten(DateTimeOffset now, TimeSpan idle)
    {
        lock (_gate)
            return _used.Count == 0 && now - _seen >= idle;
    }

    public bool IsLoaded(string skill)
    {
        lock (_gate)
            return _used.ContainsKey(skill);
    }

    /// <summary>Loads the skill; whether it was not loaded before.</summary>
    public bool TryLoad(string skill, DateTimeOffset now)
    {
        lock (_gate)
            return _used.TryAdd(skill, now);
    }

    /// <summary>Unloads the skill; whether it was loaded.</summary>
    public bool TryUnload(string skill)
    {
        lock (_gate)
            return _used.Remove(skill);
    }

    /// <summary>Marks a loaded skill used now, so expiry leaves it alone.</summary>
    public void Touch(string skill, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_used.ContainsKey(skill))
                _used[skill] = now;
        }
    }

    /// <summary>Unloads every skill unused for <paramref name="idle"/> or longer that is not busy; the ones it unloaded.</summary>
    /// <remarks>A busy skill counts as used now, so it stays a whole <paramref name="idle"/> after its work ends.</remarks>
    public IReadOnlyList<string> Expire(DateTimeOffset now, TimeSpan idle, Func<string, bool> isBusy)
    {
        lock (_gate)
        {
            List<string> unused = [.. _used.Where(each => now - each.Value >= idle).Select(each => each.Key).Order(StringComparer.Ordinal)];
            List<string> expired = [];

            foreach (var skill in unused)
            {
                if (isBusy(skill))
                {
                    _used[skill] = now;

                    continue;
                }

                _used.Remove(skill);
                expired.Add(skill);
            }

            return expired;
        }
    }

    /// <summary>Remembers that the session was heard from now.</summary>
    public void Seen(DateTimeOffset now)
    {
        lock (_gate)
            _seen = now;
    }
}

/// <summary>The skills loaded by every session of the server, each session's its own.</summary>
/// <remarks>
/// <para>
/// A session is known by <see cref="SkillOptions.SessionOf"/>, else by its MCP session id: over stdio and to a sessionless
/// client there is none, and those the server does not key share one set — what Snail.MCP.Blender gives a client it
/// cannot tell apart. Every tool, skills included, stays in the server's
/// one tool collection; a session's list leaves out the skills it has not loaded, so loading one changes the list of the
/// session that asked and of nobody else, and the protocol's own tool lookup serves every call.
/// </para>
/// <para>
/// Snail.MCP.Blender and AfterEffects gave each session a collection of its own instead, built from the methods at run time
/// (<c>McpServerTool.Create</c> with <c>ActivatorUtilities</c> over <c>Assembly.GetTypes</c>) and swapped in through the
/// HTTP transport's session hook — neither of which a native build or a stdio server has.
/// </para>
/// </remarks>
internal sealed class SkillSessions(string prefix, TimeProvider time, Func<IServiceProvider, McpServer?, string?>? sessionOf = null)
{
    private readonly ConcurrentDictionary<string, LoadedSkills> _sessions = new(StringComparer.Ordinal);

    /// <summary>The prefix of the skill tools' names, without its underscore.</summary>
    public string Prefix { get; } = prefix;

    public TimeProvider Time { get; } = time;

    /// <summary>The skills of the session <paramref name="server"/> serves, as <paramref name="services"/> name it.</summary>
    public LoadedSkills Of(McpServer server, IServiceProvider? services)
    {
        var named = services is not null && sessionOf is not null ? sessionOf(services, server) : null;
        var loaded = _sessions.GetOrAdd(named ?? server.SessionId ?? string.Empty, _ => new LoadedSkills());

        loaded.Seen(Time.GetUtcNow());

        return loaded;
    }

    /// <summary>The skills of the session a request is served in.</summary>
    public LoadedSkills Of(MessageContext context) => Of(context.Server, Sessions.SessionRegistration.ServicesOf(context));

    /// <summary>Unloads, in every session, the skills unused for <paramref name="idle"/> and not busy; the skills unloaded, each once.</summary>
    /// <remarks>
    /// A session with nothing loaded that has not been heard from for as long is forgotten, so ended sessions do not pile up.
    /// Whether a skill is busy is asked once a sweep, however many sessions loaded it.
    /// </remarks>
    public IReadOnlyList<string> Expire(TimeSpan idle, Func<string, bool>? isBusy = null)
    {
        var now = Time.GetUtcNow();
        var asked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var busy = isBusy is null ? (_ => false) : (Func<string, bool>)(skill => asked.TryGetValue(skill, out var known) ? known : asked[skill] = isBusy(skill));
        List<string> expired = [.. _sessions.Values.SelectMany(session => session.Expire(now, idle, busy)).Distinct(StringComparer.OrdinalIgnoreCase)];

        foreach (var forgotten in _sessions.Where(each => each.Value.IsForgotten(now, idle)).ToList())
            _sessions.TryRemove(forgotten);

        return expired;
    }
}

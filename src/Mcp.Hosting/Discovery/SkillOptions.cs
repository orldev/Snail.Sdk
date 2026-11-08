using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>How a server's skills are named, whose set a request loads into, and how long one stays loaded unused.</summary>
public sealed class SkillOptions
{
    /// <summary>The prefix of the skill tools' names: <c>ae</c> names them <c>ae_skills</c>, <c>ae_enable_skill</c>, <c>ae_disable_skill</c>.</summary>
    /// <remarks>
    /// The server's short name, read from the command Snail.Sdk.Mcp writes into the server's assembly
    /// (<see cref="ServerSettings.NameOf"/>) unless set: <c>s3l-mcp-blender</c> is <c>blender</c>. A program built
    /// otherwise, a test among them, sets it. Written with its underscore, as <see cref="DiscoveryOptions.Prefix"/> is
    /// (<c>ae_</c>), it names the same tools.
    /// </remarks>
    public string? Prefix { get; set; }

    /// <summary>How long a skill stays loaded in a session that calls none of its tools; never unloaded when zero, as unless set.</summary>
    /// <remarks>A long session otherwise keeps every skill it ever loaded in the model's context. The servers that unload set it from their settings.</remarks>
    public TimeSpan IdleTimeout { get; set; }

    /// <summary>Whose set of loaded skills a request loads into and lists from, from the request's services and server; the request's MCP session unless set.</summary>
    /// <remarks>
    /// <para>
    /// A client of the 2026-07-28 revision over HTTP has no MCP session, and neither has a stdio client: by the session
    /// they share one set, so a skill one client loads joins every such client's list. Snail.MCP.AfterEffects and Blender
    /// tell those clients apart by the name of the key they present — <c>(services, server) => KeyNameOf(services)</c>.
    /// </para>
    /// <para>
    /// Null from it falls back to the MCP session, so a server keys the clients it can tell apart and leaves the rest as
    /// they were. The server is the request's, null only where a skill is reached outside one.
    /// </para>
    /// <para>
    /// A name is never taken for a session's id: the two are kept apart inside, so the name needs no prefix of its own —
    /// Snail.MCP.Blender wrote <c>client:</c> before each, since a key named like a session's id shared its skills.
    /// </para>
    /// </remarks>
    public Func<IServiceProvider, McpServer?, string?>? SessionOf { get; set; }

    /// <summary>Whether a skill is at work and must stay loaded, from the name of the skill and the server's services; no skill is, unless set.</summary>
    /// <remarks>
    /// The idle expiry counts only calls: a render the <c>farm</c> skill started runs for an hour with no call of its tools,
    /// and unloading it then took the tools that follow the render out of every list (Snail.MCP.Blender, AfterEffects). A
    /// skill busy at a sweep counts as used then, so it stays loaded for a whole <see cref="IdleTimeout"/> after its work ends.
    /// </remarks>
    public Func<string, IServiceProvider, bool>? IsBusy { get; set; }
}

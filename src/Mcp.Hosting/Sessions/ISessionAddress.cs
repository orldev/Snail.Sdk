namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>What the address the session was opened at says: <c>…/mcp?root=/workspaces/task&amp;tool=read_file</c>.</summary>
/// <remarks>
/// <para>
/// A server reached over HTTP is opened at a URL, and the URL is the one place a caller states something about its whole
/// session rather than about one call. The platform writes it, the model never does: a tool call cannot reach it, which
/// is why the working copy (<see cref="SessionRoots"/>) and the tools granted (<see cref="SessionGrant"/>) travel there
/// rather than in an argument.
/// </para>
/// <para>
/// A server speaking stdio has no address: it answers nothing, and what reads it falls back to what the server was
/// configured with — how a program started for one caller has always behaved. <c>Snail.Sdk.Mcp.Http</c> registers the
/// address of the request being served; a test gives a fixed one (<c>FixedAddress</c> of <c>Snail.Sdk.Mcp.Testing</c>).
/// </para>
/// </remarks>
public interface ISessionAddress
{
    /// <summary>Whether the session was opened at an address at all: false for a server started as a program.</summary>
    /// <remarks>
    /// An address that names no tool and no address are different facts: a grant may be required of the first
    /// (<see cref="GrantOptions.RequiresGrant"/>), never of the second, since a program over stdio has nowhere to name one.
    /// </remarks>
    bool IsPresent => true;

    /// <summary>What a refusal calls the one the address speaks for: <c>session</c>, or <c>request</c> where each request stands alone.</summary>
    /// <remarks>
    /// "Not granted to this session" told a caller of a stateless server — Snail.MCP.Playwright, each request on its own —
    /// about a session it never had. The address of <c>Snail.Sdk.Mcp.Http</c> answers <c>request</c> when the transport
    /// keeps no sessions (<c>HttpServerSessionMode.Stateless</c>), so <see cref="SessionGrant"/> and
    /// <see cref="SessionRoots"/> refuse in its word without a setting.
    /// </remarks>
    string Subject => "session";

    /// <summary>Every value the address gives the parameter, empty when it gives none or there is no address.</summary>
    IReadOnlyList<string> Values(string parameter);
}

/// <summary>The addresses every server may need without a transport to read one from.</summary>
public static class SessionAddress
{
    /// <summary>No address: what a server started as a program has, and what reads it outside any request.</summary>
    /// <remarks>
    /// It answers nothing, so a grant names no tool and the roots are the configured volumes. Registered by
    /// <see cref="SessionRegistration.RefuseWhatWasNotGranted"/> and <see cref="SessionRegistration.WithSessionRoots"/>
    /// unless a transport gives one; a benchmark or a test that builds <see cref="SessionRoots"/> by hand passes it, as
    /// Snail.MCP.Documents' benchmark kept a <c>NoAddress</c> of its own to do.
    /// </remarks>
    public static ISessionAddress None { get; } = new NoAddress();

    private sealed class NoAddress : ISessionAddress
    {
        public bool IsPresent => false;

        public IReadOnlyList<string> Values(string parameter) => [];
    }
}

namespace Snail.Sdk.Mcp.Testing;

/// <summary>A server under test that exited before it answered: its exit code and what it wrote on stderr, as values a test asserts on.</summary>
/// <remarks>
/// <para>
/// A server that refuses its start for what the operator set says so in one line on stderr and exits with 1, and its
/// contract test proves both. Thrown as a plain <see cref="InvalidOperationException"/>, the start left a test the
/// message alone, so the fleet's tests parsed it — <c>" exited with 1 before "</c>, and no <c>"   at "</c> of a stack
/// trace anywhere in it (Snail.MCP.AfterEffects, Blender, CodeContext, Figma, Playwright, Roslyn; 0.5.0-local.5). Here
/// the exit code is <see cref="ExitCode"/> and the stderr is <see cref="Stderr"/>, and the message stays what it was.
/// </para>
/// <para>
/// It stays an <see cref="InvalidOperationException"/>, so a <c>catch</c> of that keeps catching it. xUnit's
/// <c>Assert.ThrowsAsync&lt;InvalidOperationException&gt;</c> asks for the type exactly and no longer matches: such a
/// test asserts <c>ServerExitedException</c>, or <c>ThrowsAnyAsync</c>.
/// </para>
/// </remarks>
public sealed class ServerExitedException : InvalidOperationException
{
    /// <summary>A server that exited with <paramref name="exitCode"/> before it answered what the start waited for.</summary>
    /// <param name="program">The command line the server was started with.</param>
    /// <param name="exitCode">The code the process exited with.</param>
    /// <param name="stderr">What the process wrote on stderr, one line after another.</param>
    /// <param name="awaited">What the start waited for: <c>GET /healthz</c>, <c>initialize</c>.</param>
    /// <param name="innerException">The failure the exit was noticed by, when there is one: the MCP SDK's closed transport over stdio.</param>
    internal ServerExitedException(string program, int exitCode, string stderr, string awaited, Exception? innerException = null)
        : base(MessageOf(program, exitCode, stderr, awaited), innerException)
    {
        Program = program;
        ExitCode = exitCode;
        Stderr = stderr;
    }

    /// <summary>The command line the server was started with.</summary>
    public string Program { get; }

    /// <summary>The code the process exited with: 1 for a start refused for what the operator set.</summary>
    public int ExitCode { get; }

    /// <summary>What the process wrote on stderr before it exited, one line after another; empty when it wrote nothing.</summary>
    /// <remarks>
    /// Over HTTP the last ten thousand lines; over stdio the tail the MCP SDK keeps. A refusal is one line, with no
    /// <c>"   at "</c> of a stack trace in it.
    /// </remarks>
    public string Stderr { get; }

    private static string MessageOf(string program, int exitCode, string stderr, string awaited)
    {
        var said = stderr.Length > 0 ? $"; its stderr:\n{stderr}" : "; it wrote nothing on stderr";

        return $"{program} exited with {exitCode} before it answered {awaited}{said}";
    }
}

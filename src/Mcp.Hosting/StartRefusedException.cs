namespace Snail.Sdk.Mcp.Hosting;

/// <summary>A start refused for what the operator set — a setting that cannot mean anything, an endpoint beyond the server's reach, a secret's file that holds something else — in one sentence that names it.</summary>
/// <remarks>
/// <para>
/// The message is the whole of it: it names the variable or the file and what to set, and no stack trace says more to an
/// operator. Thrown as an <see cref="InvalidOperationException"/>, it ended the process unhandled, the trace on stderr
/// and exit code 134 — a crash, where a person had only set a wrong value (the fleet on 0.5.0-local.4). A program
/// catches this one type around its start, writes the message to stderr as one line and exits with 1, as the program of
/// <c>dotnet new s3l-mcp</c> does:
/// <c>catch (StartRefusedException refused) { await Console.Error.WriteLineAsync(refused.Message); return 1; }</c>.
/// </para>
/// <para>
/// It stays an <see cref="InvalidOperationException"/>, so a server that catches that keeps catching it.
/// </para>
/// </remarks>
public class StartRefusedException : InvalidOperationException
{
    /// <summary>A start refused, in a sentence that names what the operator set and what it can be.</summary>
    /// <param name="message">What was set, and what it can be instead: <c>SNAIL_MCP_DEMO_PORT is 'x'; it is a port number, or 0 for stdio.</c></param>
    public StartRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>A start refused, with the failure that showed it.</summary>
    /// <param name="message">What was set, and what it can be instead.</param>
    /// <param name="innerException">The failure that showed the setting to be wrong.</param>
    public StartRefusedException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>A start refused, with no word on why.</summary>
    public StartRefusedException()
        : this("The server's start was refused.")
    {
    }
}

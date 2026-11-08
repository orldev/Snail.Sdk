using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>One command a program sends, with what it is sent with: what <see cref="IProgramCommands.CallAsync(CommandCall, CancellationToken)"/> is handed.</summary>
/// <remarks>
/// A record rather than more parameters, so that what a command learns of its program can grow without another overload
/// on every catalog. Not to be confused with <see cref="ProgramCall"/>, the line of the trace the command leaves behind.
/// </remarks>
/// <param name="Command">One of <see cref="IProgramCommands.Names"/>.</param>
/// <param name="Arguments">The object the program passed, empty when it passed none.</param>
/// <param name="Request">The request of <c>run_program</c> whose program sent the command; null when <see cref="ProgramRunner"/> was given none.</param>
/// <param name="Deadline">When the program runs out of time, and the command with it: its cancellation comes then.</param>
public sealed record CommandCall(string Command, JsonObject Arguments, RequestContext<CallToolRequestParams>? Request, DateTimeOffset Deadline)
{
    /// <summary>How long the program has left as this is read, never less than zero: the longest a command may wait.</summary>
    public TimeSpan Remaining
    {
        get
        {
            var left = Deadline - DateTimeOffset.UtcNow;

            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }
}

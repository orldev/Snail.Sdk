using System.Text.Json.Nodes;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>The commands a program calls on its global object: <c>git.status()</c>, <c>blender.add_primitive({ … })</c>.</summary>
/// <remarks>
/// <para>
/// The program's only door to the outside. A server with a catalog of its own (Snail.MCP.Git, Snail.MCP.Blender,
/// Snail.MCP.AfterEffects) implements it over that catalog; a server whose commands are its tools takes
/// <see cref="ToolCommands"/>. Resolved from the services of the request, so a catalog that depends on the session
/// sees the session that called <c>run_program</c>.
/// </para>
/// <para>
/// Before every command the session's gate is asked for the tool the command stands for (<see cref="ToolOf"/>): the door
/// sees <c>run_program</c> and nothing it sends, and a role granted <c>git_status</c> and <c>run_program</c> pushed
/// through Snail.MCP.Git until its catalog asked the grant itself.
/// </para>
/// </remarks>
public interface IProgramCommands
{
    /// <summary>The commands a program may call, in the order the answers list them.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>The tool a command stands for, under whose name the session's gate grants it: <c>status</c> is <c>git_status</c>; null when it stands for none.</summary>
    /// <remarks>
    /// Asked before every command the program sends. Not every command has a tool: two of Snail.MCP.AfterEffects' 129
    /// bridge commands have none. Null says so, and the gate (<c>IToolGate</c>) is asked the command's own name
    /// instead — the one question for both, so a gate that grants by scope judges the name as a command, and the default
    /// gate grants it as it grants a tool: a session whose address names tools sends the command only when it is named too.
    /// A catalog never escapes the gate by answering null.
    /// </remarks>
    string? ToolOf(string command);

    /// <summary>Runs one command the program sent, with all it was sent with — the request of <c>run_program</c>, the time the program has left — answering with its data or why it failed.</summary>
    /// <remarks>
    /// <para>
    /// A catalog that needs more than the command and its arguments reads it here: the session, the services of the
    /// request and the grant are on <see cref="CommandCall.Request"/>, and a command that waits on something slow — a
    /// bridge into another application — takes its own timeout from <see cref="CommandCall.Remaining"/> rather than a
    /// ceiling of its own. Snail.MCP.AfterEffects waited the longest a program may run for every command, since nothing
    /// told it the time this program had left.
    /// </para>
    /// <para>
    /// A failure reaches the program as an exception it may catch — <c>{ error, message, hint, type, details }</c>, the
    /// answer's <see cref="Answer{T}.Details"/> as <c>details</c> — so the answer is the fleet's <see cref="Answer{T}"/>
    /// rather than a type of this package. What a command is sent with grows on <see cref="CommandCall"/>, never as
    /// another overload every catalog would have to carry.
    /// </para>
    /// </remarks>
    /// <param name="call">The command, its arguments, the request and the program's deadline.</param>
    /// <param name="cancellationToken">Ends the command when the program runs out of time or its call is cancelled.</param>
    Task<Answer<JsonNode?>> CallAsync(CommandCall call, CancellationToken cancellationToken);
}

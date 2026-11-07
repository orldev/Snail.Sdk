using System.Text.Json.Nodes;

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

    /// <summary>The tool a command stands for, under whose name the session's gate grants it: <c>status</c> is <c>git_status</c>.</summary>
    string ToolOf(string command);

    /// <summary>Runs one command with the arguments the program passed, answering with its data or why it failed.</summary>
    /// <param name="command">One of <see cref="Names"/>.</param>
    /// <param name="arguments">The object the program passed, empty when it passed none.</param>
    /// <param name="cancellationToken">Ends the command when the program runs out of time or its call is cancelled.</param>
    Task<ProgramAnswer> CallAsync(string command, JsonObject arguments, CancellationToken cancellationToken);
}

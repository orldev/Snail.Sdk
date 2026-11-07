using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>The commands of a server whose commands are its tools: every tool but the forwarders, called as the protocol would call it.</summary>
/// <remarks>
/// <para>
/// For a server without a catalog of its own: <c>.WithPrograms&lt;ToolCommands&gt;(options =&gt; options.Global = "notes")</c>
/// and a program calls <c>notes.read({ id: 3 })</c> for the tool <c>notes_read</c>. A tool named after the global and an
/// underscore is the command without them, as the fleet's catalogs name their commands; any other tool keeps its name.
/// </para>
/// <para>
/// Each command goes through <see cref="ToolForwarding.CallAsync"/>: the session's gate, the tool's own schema and filters
/// of the request, and no forwarder — <c>run_tools</c>, <c>run_program</c> — among the commands, since a forwarder run by
/// a program would be twenty steps of twenty steps. A tool answers as it answers a client, read by <see cref="ProgramAnswer.From"/>.
/// </para>
/// </remarks>
public sealed class ToolCommands : IProgramCommands
{
    private readonly RequestContext<CallToolRequestParams> _context;
    private readonly string _prefix;
    private readonly Dictionary<string, McpServerTool> _tools = new(StringComparer.Ordinal);

    /// <summary>The tools of the server <paramref name="context"/> was sent to, named without <paramref name="prefix"/>.</summary>
    /// <param name="context">The request of <c>run_program</c>: its session, services and grant serve every command.</param>
    /// <param name="prefix">What a tool's name starts with that its command leaves out, such as <c>notes_</c>; nothing when null.</param>
    public ToolCommands(RequestContext<CallToolRequestParams> context, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _prefix = prefix ?? string.Empty;

        foreach (var tool in context.Server.ServerOptions.ToolCollection ?? [])
        {
            if (!ToolForwarding.IsForwarder(tool))
                _tools[CommandOf(tool.ProtocolTool.Name)] = tool;
        }

        Names = [.. _tools.Keys.Order(StringComparer.Ordinal)];
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Names { get; }

    /// <inheritdoc/>
    public string ToolOf(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return _tools.TryGetValue(command, out var tool) ? tool.ProtocolTool.Name : command;
    }

    /// <inheritdoc/>
    public async Task<ProgramAnswer> CallAsync(string command, JsonObject arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_tools.TryGetValue(command, out var tool))
            return ProgramAnswer.Failed($"no command '{command}'", $"the commands are {string.Join(", ", Names)}", ProgramFailures.NoSuchCommand);

        var result = await ToolForwarding.CallAsync(_context, tool, arguments, cancellationToken).ConfigureAwait(false);

        return ProgramAnswer.From(result);
    }

    private string CommandOf(string tool) =>
        _prefix.Length > 0 && tool.Length > _prefix.Length && tool.StartsWith(_prefix, StringComparison.Ordinal) ? tool[_prefix.Length..] : tool;
}

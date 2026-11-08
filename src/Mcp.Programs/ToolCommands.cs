using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
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
/// a program would be twenty steps of twenty steps. A tool answers as it answers a client, read as the envelope it answers in.
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
    public async Task<Answer<JsonNode?>> CallAsync(CommandCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);

        if (!_tools.TryGetValue(call.Command, out var tool))
            return Answer.Failed($"no command '{call.Command}'", $"the commands are {string.Join(", ", Names)}", ProgramFailures.NoSuchCommand);

        CallToolResult result = await ToolForwarding.CallAsync(_context, tool, call.Arguments, cancellationToken).ConfigureAwait(false);

        return AnswerOf(result);
    }

    /// <summary>A tool's result as a program reads it: the data of the envelope, or its failure.</summary>
    /// <remarks>
    /// A result that is not the envelope is read as well as it can be: its structured content, else its text as JSON, else
    /// its text as a string. A result flagged <c>isError</c> without the envelope — a refusal of the door — fails with its text.
    /// </remarks>
    internal static Answer<JsonNode?> AnswerOf(CallToolResult result)
    {
        string text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        JsonNode? parsed = Parsed(text);

        if (ToolAnswer.IsFailure(result) && parsed is JsonObject failure)
            return Answer.Failed(Text(failure, "error") ?? "the tool failed", Text(failure, "hint"), Text(failure, "type"), failure["data"]?.DeepClone());

        if (result.IsError == true)
            return Answer.Failed(text.Length > 0 ? text : "the tool failed");

        if (parsed is JsonObject envelope && envelope["ok"]?.GetValueKind() == JsonValueKind.True)
            return Answer.Ok<JsonNode?>(envelope["data"]?.DeepClone());

        if (result.StructuredContent is { } structured)
            return Answer.Ok<JsonNode?>(JsonNode.Parse(structured.GetRawText()));

        return Answer.Ok<JsonNode?>(parsed ?? JsonValue.Create(text));
    }

    private string CommandOf(string tool) =>
        _prefix.Length > 0 && tool.Length > _prefix.Length && tool.StartsWith(_prefix, StringComparison.Ordinal) ? tool[_prefix.Length..] : tool;

    private static string? Text(JsonObject envelope, string name) =>
        envelope[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>The text as JSON, or null for text that is not: a tool that answers plain text is read as a string.</summary>
    private static JsonNode? Parsed(string text)
    {
        try
        {
            return text.Length == 0 ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

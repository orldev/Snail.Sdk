using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Extensions.DependencyInjection;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>What is wrong with the arguments of a call, read from the tool's own input schema before the tool runs.</summary>
/// <remarks>
/// A call that leaves out a required parameter never reaches the tool: binding it throws inside the MCP SDK, and the caller
/// reads "An error occurred invoking 'find_tool'", which names neither the parameter nor the mistake. A month of
/// Snail.MCP.Blender's logs held seven such calls — <c>query</c> and <c>task</c> sent for <c>intent</c>, <c>program</c>
/// for <c>code</c>, <c>path</c> for <c>remotePath</c> — and a parameter the tool does not take was dropped without a
/// word, so the call did something other than was asked. Each cost a round trip to a model that could only guess again.
/// </remarks>
internal static class ArgumentCheck
{
    /// <summary>The <c>type</c> of the answer to a call whose arguments do not fit.</summary>
    public const string Type = "arguments";

    /// <summary>Names offered for one unknown parameter; past two, a list reads as noise rather than as help.</summary>
    private const int Suggestions = 2;

    /// <summary>Why this call cannot be made, or null when its arguments fit the schema.</summary>
    /// <param name="tool">The tool's name.</param>
    /// <param name="schema">The tool's input schema.</param>
    /// <param name="sent">The names the call sent.</param>
    /// <param name="accepted">Names the tool takes beside its schema, as <c>find_tool</c> takes <c>query</c>.</param>
    /// <param name="synonyms">The parameters a wrong name may stand for (<see cref="ArgumentCheckOptions.Synonyms"/>).</param>
    public static string? Refusal(string tool, JsonElement schema, IEnumerable<string>? sent, IEnumerable<string>? accepted = null, IDictionary<string, IReadOnlyList<string>>? synonyms = null)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return null;

        var declared = properties.EnumerateObject().Select(property => property.Name).ToList();
        var taken = new HashSet<string>([.. declared, .. accepted ?? []], StringComparer.Ordinal);
        var given = (sent ?? []).ToList();
        var missing = ToolCatalog.RequiredOf(schema).Where(name => !given.Contains(name, StringComparer.Ordinal)).ToList();
        IReadOnlyList<string> unknown = IsOpen(schema) ? [] : [.. given.Where(name => !taken.Contains(name))];

        if (missing.Count == 0 && unknown.Count == 0)
            return null;

        return Sentence(tool, missing, unknown, declared, synonyms);
    }

    /// <summary>The names a tool takes beside its schema, which its method declares with <see cref="AcceptsAttribute"/>.</summary>
    public static IReadOnlyList<string> AcceptedBy(McpServerTool tool) => [.. tool.Metadata.OfType<AcceptsAttribute>().SelectMany(accepts => accepts.Names)];

    /// <summary>What the tool takes, for the hint of a refusal.</summary>
    public static string Takes(string tool, JsonElement schema)
    {
        var parameters = ToolCatalog.ParametersOf(schema);

        return parameters.Count == 0
            ? $"'{tool}' takes no arguments."
            : $"'{tool}' takes {string.Join(", ", parameters.Select(parameter => parameter.IsRequired ? $"{parameter.Name} (required)" : parameter.Name))}.";
    }

    /// <summary>The names closest to one that is not among them, nearest first; empty when none is close enough to be a guess.</summary>
    /// <remarks>
    /// A name that spells another badly is close by edits — <c>colour</c> for <c>color</c> — within a third of its length
    /// and at least two; a name that is the short form of another is close by being inside it, which is how <c>timeout</c>
    /// finds <c>timeoutSeconds</c> and <c>path</c> finds <c>remotePath</c> although many edits part them. A name that is
    /// neither offers nothing rather than the first in the alphabet: a wrong suggestion is worse than none.
    /// </remarks>
    public static IReadOnlyList<string> Nearest(string name, IEnumerable<string> candidates)
    {
        var allowed = Math.Max(2, name.Length / 3);
        var scored = candidates.Select(candidate => (Name: candidate, Distance: IsShortFormOf(name, candidate) ? 1 : Distance(name, candidate)));
        var close = scored.Where(candidate => candidate.Distance <= allowed).OrderBy(candidate => candidate.Distance);

        return [.. close.ThenBy(candidate => candidate.Name, StringComparer.Ordinal).Take(Suggestions).Select(candidate => candidate.Name)];
    }

    /// <summary>The call refused when its arguments do not fit, handed on otherwise.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Checking(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is not McpServerTool tool)
                return await next(context, cancellationToken).ConfigureAwait(false);

            var name = tool.ProtocolTool.Name;
            var schema = tool.ProtocolTool.InputSchema;

            var synonyms = SessionRegistration.ServicesOf(context)?.GetService<Checked>()?.Options.Synonyms;

            if (Refusal(name, schema, context.Params?.Arguments?.Keys, AcceptedBy(tool), synonyms) is { } wrong)
                return ToolAnswer.Failed(wrong, Takes(name, schema), Type);

            return await next(context, cancellationToken).ConfigureAwait(false);
        };

    /// <summary>Whether the schema takes names it does not declare.</summary>
    private static bool IsOpen(JsonElement schema) =>
        schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind is JsonValueKind.True or JsonValueKind.Object;

    private static string Sentence(string tool, IReadOnlyList<string> missing, IReadOnlyList<string> unknown, IReadOnlyList<string> declared, IDictionary<string, IReadOnlyList<string>>? synonyms)
    {
        var needs = missing.Count == 0 ? null : $"needs {Quoted(missing)}, which this call did not send";
        var takes = unknown.Count == 0 ? null : $"takes no {string.Join(", ", unknown.Select(name => Described(name, declared, synonyms)))}";
        var both = needs is null ? takes : takes is null ? needs : $"{needs}, and {takes}";

        return $"'{tool}' {both}.";
    }

    private static string Described(string name, IReadOnlyList<string> declared, IDictionary<string, IReadOnlyList<string>>? synonyms) =>
        Meant(name, declared, synonyms) is { Count: > 0 } nearest
            ? $"'{name}' (did you mean {string.Join(" or ", nearest.Select(hint => $"'{hint}'"))}?)"
            : $"'{name}'";

    /// <summary>The parameters of the tool a wrong name stands for: its synonyms the tool takes, else the names nearest to it.</summary>
    private static IReadOnlyList<string> Meant(string name, IReadOnlyList<string> declared, IDictionary<string, IReadOnlyList<string>>? synonyms)
    {
        if (synonyms is not null && synonyms.TryGetValue(name, out var meant))
        {
            List<string> taken = [.. meant.Where(synonym => declared.Contains(synonym, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).Take(Suggestions)];

            if (taken.Count > 0)
                return taken;
        }

        return Nearest(name, declared);
    }

    private static string Quoted(IEnumerable<string> names) => string.Join(", ", names.Select(name => $"'{name}'"));

    private static bool IsShortFormOf(string name, string candidate) =>
        name.Length >= 3 && candidate.Contains(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>How many single-character edits turn one name into the other, ignoring case.</summary>
    private static int Distance(string from, string to)
    {
        var previous = new int[to.Length + 1];
        var current = new int[to.Length + 1];

        for (var index = 0; index <= to.Length; index++)
            previous[index] = index;

        for (var row = 1; row <= from.Length; row++)
        {
            current[0] = row;

            for (var column = 1; column <= to.Length; column++)
            {
                var substitution = previous[column - 1] + (char.ToLowerInvariant(from[row - 1]) == char.ToLowerInvariant(to[column - 1]) ? 0 : 1);

                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[to.Length];
    }

    /// <summary>The mark that the check is registered, with its options, which <c>run_tools</c> reads to check its steps alike.</summary>
    internal sealed class Checked(ArgumentCheckOptions options)
    {
        public ArgumentCheckOptions Options { get; } = options;
    }
}

/// <summary>Names a tool takes beside the parameters of its schema, which the argument check lets through: <c>find_tool</c>'s <c>query</c>.</summary>
/// <remarks>Declared in the schema, every name costs every session the bytes of its description; a name only some clients send is taken without it.</remarks>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class AcceptsAttribute(params string[] names) : Attribute
{
    public IReadOnlyList<string> Names { get; } = names;
}

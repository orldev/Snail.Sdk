using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>The guard rules every Snail server's tools keep, checked on the list a client receives.</summary>
/// <remarks>
/// A model knows a tool only by what tools/list says, so what is checked is the wire copy: a name in snake case and
/// unique, a description for the tool and for every parameter, the effect stated (read-only or destructive), every
/// allowed value of a parameter or of its array's items named as a word of its own where the model reads it, and English
/// throughout. A test asserts the answer is empty:
/// <c>Assert.Empty(ToolGuards.Problems(tools.Select(tool =&gt; tool.ProtocolTool)))</c>.
/// </remarks>
public static partial class ToolGuards
{
    /// <summary>Every rule a tool breaks, one line each; empty when all hold.</summary>
    public static IReadOnlyList<string> Problems(IEnumerable<Tool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var listed = tools.ToList();
        var problems = new List<string>();
        var repeated = listed.GroupBy(tool => tool.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key);

        problems.AddRange(repeated.Select(name => $"{name}: listed more than once"));

        foreach (var tool in listed)
            problems.AddRange(Problems(tool));

        return problems;
    }

    private static IEnumerable<string> Problems(Tool tool)
    {
        if (!SnakeCase().IsMatch(tool.Name))
            yield return $"{tool.Name}: the name is not snake case";

        if (string.IsNullOrWhiteSpace(tool.Description))
            yield return $"{tool.Name}: no description";
        else if (NotEnglish().IsMatch(tool.Description))
            yield return $"{tool.Name}: the description is not in English";

        if (tool.Annotations is not { ReadOnlyHint: not null } and not { DestructiveHint: not null })
            yield return $"{tool.Name}: does not say whether it changes anything (ReadOnly or Destructive)";

        if (!tool.InputSchema.TryGetProperty("properties", out var properties))
            yield break;

        if (properties.ValueKind is not JsonValueKind.Object)
        {
            yield return $"{tool.Name}: properties is {properties.ValueKind}, not an object of parameters";

            yield break;
        }

        foreach (var parameter in properties.EnumerateObject())
        {
            foreach (var problem in ParameterProblems(tool, parameter))
                yield return problem;
        }
    }

    private static IEnumerable<string> ParameterProblems(Tool tool, JsonProperty parameter)
    {
        var description = parameter.Value.TryGetProperty("description", out var text) ? text.GetString() ?? string.Empty : string.Empty;

        if (description.Length == 0)
            yield return $"{tool.Name}.{parameter.Name}: no description";
        else if (NotEnglish().IsMatch(description))
            yield return $"{tool.Name}.{parameter.Name}: the description is not in English";

        var readable = $"{description} {tool.Description}";
        var unnamed = AllowedValues(parameter.Value)
            .Where(value => value.Length > 0 && !Names(readable, value))
            .ToList();

        if (unnamed.Count > 0)
            yield return $"{tool.Name}.{parameter.Name}: the descriptions never name {string.Join(", ", unnamed)}";
    }

    /// <summary>The values a parameter allows: its own <c>enum</c>, and the <c>enum</c> of its items when it is an array.</summary>
    private static IEnumerable<string> AllowedValues(JsonElement parameter)
    {
        if (parameter.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
                yield return value.ToString();
        }

        if (parameter.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object
            && items.TryGetProperty("enum", out var itemValues) && itemValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in itemValues.EnumerateArray())
                yield return value.ToString();
        }
    }

    /// <summary>Whether the text names the value as a word of its own, not as a part of a longer one.</summary>
    private static bool Names(string text, string value)
    {
        for (var at = text.IndexOf(value, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsWordCharacter(text, at - 1) && !IsWordCharacter(text, at + value.Length))
                return true;
        }

        return false;
    }

    private static bool IsWordCharacter(string text, int index) =>
        index >= 0 && index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '-');

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$")]
    private static partial Regex SnakeCase();

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex NotEnglish();
}

using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>Whether a tool's answer is what its output schema says it is: what a test asserts before a client that validates the structured content refuses it.</summary>
/// <remarks>
/// <para>
/// A tool that states an output schema promises structured content that fits it, and a client of the TypeScript SDK checks
/// every result against it, an error's too: an answer that does not fit is refused there, never in the server's own tests,
/// which read the text (Snail.MCP.Figma found a published <c>data</c> that allowed null only by reading the schema).
/// </para>
/// <para>
/// The check is light and needs nothing beside the BCL: <c>type</c> (a list of them too, <c>integer</c> a whole number),
/// <c>properties</c>, <c>required</c>, <c>additionalProperties</c>, <c>items</c>, <c>enum</c>, <c>const</c>,
/// <c>anyOf</c>/<c>oneOf</c> (one branch fitting is enough), <c>allOf</c> and a <c>$ref</c> within the schema. Formats,
/// patterns, lengths and bounds are not checked: they are the server's own words about a value, not its shape.
/// </para>
/// </remarks>
public static class OutputSchemas
{
    /// <summary>What in the answer does not fit the tool's output schema, one line each, the place named as a path from <c>$</c>; none when it fits.</summary>
    /// <remarks>
    /// A tool without an output schema has nothing to fit. One with a schema owes every success structured content, so a
    /// success without it is a problem; a failure may carry none, and when it carries some, it is checked like any other.
    /// </remarks>
    /// <param name="tool">The tool as it is listed: <c>McpClientTool.ProtocolTool</c>, <c>McpServerTool.ProtocolTool</c>.</param>
    /// <param name="result">What it answered.</param>
    public static IReadOnlyList<string> Problems(Tool tool, CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(result);

        var problems = new List<string>();

        if (tool.OutputSchema is not { } schema)
            return problems;

        if (result.StructuredContent is not { } structured)
        {
            if (result.IsError != true)
                problems.Add($"$: '{tool.Name}' states an output schema, and this success carries no structured content");

            return problems;
        }

        Check(structured, schema, schema, "$", problems);

        return problems;
    }

    private static void Check(JsonElement value, JsonElement schema, JsonElement root, string path, List<string> problems)
    {
        if (schema.ValueKind == JsonValueKind.False)
            problems.Add($"{path}: the schema allows nothing here");

        if (schema.ValueKind != JsonValueKind.Object)
            return;

        if (schema.TryGetProperty("$ref", out var reference) && Resolved(reference, root) is { } referred)
            Check(value, referred, root, path, problems);

        if (schema.TryGetProperty("allOf", out var all) && all.ValueKind == JsonValueKind.Array)
        {
            foreach (var each in all.EnumerateArray())
                Check(value, each, root, path, problems);
        }

        if (!FitsBranches(value, schema, root, path, "anyOf", problems) || !FitsBranches(value, schema, root, path, "oneOf", problems))
            return;

        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(value, constant))
            problems.Add($"{path}: is {Shown(value)}, the schema takes only {constant.GetRawText()}");

        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array && !allowed.EnumerateArray().Any(word => JsonElement.DeepEquals(value, word)))
            problems.Add($"{path}: is {Shown(value)}, the schema takes one of {allowed.GetRawText()}");

        if (TypesOf(schema) is { Count: > 0 } types && !types.Any(type => IsOfType(value, type)))
        {
            problems.Add($"{path}: is {KindOf(value)}, the schema takes {string.Join(" or ", types)}");

            return;
        }

        if (value.ValueKind == JsonValueKind.Object)
            CheckObject(value, schema, root, path, problems);
        else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
            CheckItems(value, items, root, path, problems);
    }

    private static void CheckObject(JsonElement value, JsonElement schema, JsonElement root, string path, List<string> problems)
    {
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Where(name => name.ValueKind == JsonValueKind.String).Select(name => name.GetString()!))
            {
                if (!value.TryGetProperty(name, out _))
                    problems.Add($"{path}: lacks '{name}', which the schema requires");
            }
        }

        var properties = schema.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object ? declared : default;
        var additional = schema.TryGetProperty("additionalProperties", out var others) ? others : default;

        foreach (var property in value.EnumerateObject())
        {
            var at = $"{path}.{property.Name}";

            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var its))
                Check(property.Value, its, root, at, problems);
            else if (additional.ValueKind == JsonValueKind.False)
                problems.Add($"{at}: the schema allows no property of this name");
            else if (additional.ValueKind == JsonValueKind.Object)
                Check(property.Value, additional, root, at, problems);
        }
    }

    private static void CheckItems(JsonElement value, JsonElement items, JsonElement root, string path, List<string> problems)
    {
        var index = 0;

        foreach (var item in value.EnumerateArray())
        {
            Check(item, items, root, $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]", problems);
            index++;
        }
    }

    /// <summary>Whether the value fits one of the branches the schema names under <paramref name="keyword"/>, or the schema names none; a problem is added when it fits none.</summary>
    private static bool FitsBranches(JsonElement value, JsonElement schema, JsonElement root, string path, string keyword, List<string> problems)
    {
        if (!schema.TryGetProperty(keyword, out var branches) || branches.ValueKind != JsonValueKind.Array)
            return true;

        foreach (var branch in branches.EnumerateArray())
        {
            var found = new List<string>();
            Check(value, branch, root, path, found);

            if (found.Count == 0)
                return true;
        }

        problems.Add($"{path}: is {Shown(value)}, which fits none of the schema's {keyword}");

        return false;
    }

    /// <summary>The schema a <c>$ref</c> within the document points at, <c>#/$defs/Node</c>; null for one it cannot follow.</summary>
    private static JsonElement? Resolved(JsonElement reference, JsonElement root)
    {
        if (reference.ValueKind != JsonValueKind.String || reference.GetString() is not { } pointer || !pointer.StartsWith('#'))
            return null;

        var at = root;

        foreach (var segment in pointer[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(segment).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);

            if (at.ValueKind == JsonValueKind.Object && at.TryGetProperty(name, out var next))
                at = next;
            else if (at.ValueKind == JsonValueKind.Array && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < at.GetArrayLength())
                at = at[index];
            else
                return null;
        }

        return at;
    }

    private static List<string> TypesOf(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type))
            return [];

        return type.ValueKind switch
        {
            JsonValueKind.String => [type.GetString()!],
            JsonValueKind.Array => [.. type.EnumerateArray().Where(each => each.ValueKind == JsonValueKind.String).Select(each => each.GetString()!)],
            _ => [],
        };
    }

    private static bool IsOfType(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && (value.TryGetInt64(out _) || (value.TryGetDecimal(out var number) && number == decimal.Truncate(number))),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    private static string KindOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => $"the number {value.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null",
    };

    /// <summary>A value as a problem quotes it, cut short when long.</summary>
    private static string Shown(JsonElement value)
    {
        var text = value.GetRawText();

        return text.Length <= 60 ? text : $"{text[..60]}…";
    }
}

using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Discovery;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>Whether a tool that threw was never reached because the MCP SDK could not bind its arguments, and the answer that says so.</summary>
/// <remarks>
/// <para>
/// The MCP SDK binds each argument to its parameter before the tool runs and throws when one does not fit: a
/// <see cref="JsonException"/> for a value of the wrong kind (<c>"many"</c> for an <c>int</c>), an
/// <see cref="ArgumentException"/> for a required parameter left out. Answered as a failure inside the server — type
/// <c>internal</c>, "try once more" — the call was repeated as it was (Snail.MCP.Documents).
/// </para>
/// <para>
/// A tool's own code throws the same exceptions, so the type alone proves nothing. The call is held against the tool's
/// input schema, which the SDK built from the same parameters: a required parameter missing, or a value whose kind the
/// schema does not allow, is the call's fault and is named. When the schema finds nothing, a trace that runs through the
/// SDK's parameter marshalling is the only other proof; the answer then names no parameter. Anything else stays the
/// server's own failure.
/// </para>
/// </remarks>
internal static class ArgumentFaults
{
    /// <summary>The frame of the MCP SDK's parameter binding — Microsoft.Extensions.AI's <c>AIFunctionFactory</c> — in the trace of what it throws.</summary>
    private const string Marshalling = "GetParameterMarshaller";

    /// <summary>How much of a value the answer quotes.</summary>
    private const int ShownLength = 60;

    /// <summary>The refusal of a call the tool never ran for because its arguments did not bind, or null when the failure is not that.</summary>
    /// <remarks>Its words alone: <c>ExplainFailures</c> answers them as the envelope, or as plain text for a server whose answers are text.</remarks>
    public static AnswerFailure? FaultOf(RequestContext<CallToolRequestParams> request, Exception failure)
    {
        if (failure is not (JsonException or ArgumentException) || request.MatchedPrimitive is not McpServerTool tool)
            return null;

        var name = tool.ProtocolTool.Name;
        var schema = tool.ProtocolTool.InputSchema;
        var arguments = request.Params?.Arguments ?? new Dictionary<string, JsonElement>();
        var takes = ArgumentCheck.Takes(name, schema);

        if (Missing(schema, arguments) is { Count: > 0 } missing)
            return new AnswerFailure($"'{name}' needs {string.Join(", ", missing.Select(parameter => $"'{parameter}'"))}, which this call did not send.") { Hint = takes, Type = ArgumentCheck.Type };

        if (Misfit(schema, arguments) is var (parameter, value, property))
            return new AnswerFailure($"'{name}' cannot take {Shown(value)} for '{parameter}': it takes {Expected(property)}.") { Hint = $"Send '{parameter}' as {Expected(property)}. {takes}", Type = ArgumentCheck.Type };

        if (failure.StackTrace?.Contains(Marshalling, StringComparison.Ordinal) == true)
            return new AnswerFailure($"'{name}' could not read the arguments of this call: {failure.Message}") { Hint = $"Check the value of each argument against what the tool takes. {takes}", Type = ArgumentCheck.Type };

        return null;
    }

    private static List<string> Missing(JsonElement schema, IDictionary<string, JsonElement> arguments) =>
        [.. ToolCatalog.RequiredOf(schema).Where(parameter => !arguments.ContainsKey(parameter))];

    /// <summary>The first argument whose value its parameter's schema does not allow, with that schema.</summary>
    private static (string Parameter, JsonElement Value, JsonElement Property)? Misfit(JsonElement schema, IDictionary<string, JsonElement> arguments)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var (parameter, value) in arguments)
        {
            if (properties.TryGetProperty(parameter, out var property) && !Fits(value, property))
                return (parameter, value, property);
        }

        return null;
    }

    /// <summary>Whether a value is one the schema allows, read as leniently as the binding reads it: a number may come as a string, null binds to anything.</summary>
    private static bool Fits(JsonElement value, JsonElement property)
    {
        if (property.ValueKind != JsonValueKind.Object || value.ValueKind == JsonValueKind.Null)
            return true;

        if (property.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array)
            return value.ValueKind != JsonValueKind.String || allowed.EnumerateArray().Any(word => word.ValueKind == JsonValueKind.String && string.Equals(word.GetString(), value.GetString(), StringComparison.OrdinalIgnoreCase));

        var types = TypesOf(property);

        return types.Count == 0 || types.Any(type => FitsType(value, type, property));
    }

    private static bool FitsType(JsonElement value, string type, JsonElement property) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String && FitsFormat(value.GetString()!, property),
        "integer" => value.ValueKind == JsonValueKind.Number ? value.TryGetInt64(out _) : IsNumberText(value, NumberStyles.Integer),
        "number" => value.ValueKind == JsonValueKind.Number || IsNumberText(value, NumberStyles.Float),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "array" => value.ValueKind == JsonValueKind.Array && (!property.TryGetProperty("items", out var items) || value.EnumerateArray().All(item => Fits(item, items))),
        "object" => value.ValueKind == JsonValueKind.Object,
        "null" => false,
        _ => true,
    };

    /// <summary>Whether a string reads as the format its schema names; a format this does not know is taken on trust.</summary>
    private static bool FitsFormat(string text, JsonElement property)
    {
        if (!property.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String)
            return true;

        return format.GetString() switch
        {
            "date-time" => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            "uuid" => Guid.TryParse(text, out _),
            _ => true,
        };
    }

    private static bool IsNumberText(JsonElement value, NumberStyles styles) =>
        value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), styles, CultureInfo.InvariantCulture, out _);

    private static List<string> TypesOf(JsonElement property)
    {
        if (!property.TryGetProperty("type", out var type))
            return [];

        return type.ValueKind switch
        {
            JsonValueKind.String => [type.GetString()!],
            JsonValueKind.Array => [.. type.EnumerateArray().Where(each => each.ValueKind == JsonValueKind.String).Select(each => each.GetString()!)],
            _ => [],
        };
    }

    /// <summary>What a parameter takes, in words: <c>an integer</c>, <c>one of 'debug', 'release'</c>.</summary>
    private static string Expected(JsonElement property)
    {
        if (property.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array)
            return $"one of {string.Join(", ", allowed.EnumerateArray().Select(word => $"'{word}'"))}";

        if (property.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String)
        {
            switch (format.GetString())
            {
                case "date-time":
                    return "a date and time in ISO 8601, such as 2026-11-08T10:00:00Z";
                case "uuid":
                    return "a UUID";
            }
        }

        var types = TypesOf(property).Where(type => type != "null").Select(type => type switch
        {
            "string" => "a string",
            "integer" => "a whole number",
            "number" => "a number",
            "boolean" => "true or false",
            "array" => "an array",
            "object" => "an object",
            _ => type,
        });

        return string.Join(" or ", types) is { Length: > 0 } words ? words : "another value";
    }

    /// <summary>A value as the answer quotes it, cut short when long.</summary>
    private static string Shown(JsonElement value)
    {
        var text = value.GetRawText();

        return text.Length <= ShownLength ? text : $"{text[..ShownLength]}…";
    }
}

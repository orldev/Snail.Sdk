using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Discovery;

/// <summary>Tools as entries discovery searches: read from the tools a server registers, never written a second time beside them.</summary>
/// <remarks>
/// <para>
/// The parameters come from each tool's input schema — the names, types and descriptions a client receives in
/// <c>tools/list</c> — so an entry cannot drift from the tool. Five servers of the fleet kept an index of their own
/// built from the methods' parameters, each with a guard test keeping its list of classes the classes there were.
/// </para>
/// <para>
/// <c>find_tool</c> reads the tools the server registered, which is what a server needs. <see cref="Of(Type, IServiceProvider?, JsonSerializerOptions?)"/>
/// reads one class without a server — for a guard test, or a catalog of tools the server does not register.
/// </para>
/// </remarks>
public static class ToolCatalog
{
    /// <summary>The entries of the tools, in the order given, each with the command <paramref name="commandOf"/> names for it.</summary>
    /// <param name="tools">The tools, as the server registered them.</param>
    /// <param name="commandOf">What a sequence or a program calls a tool, by the tool's name; null for a tool that is no command.</param>
    public static IReadOnlyList<ToolEntry> Of(IEnumerable<McpServerTool> tools, Func<string, string?>? commandOf)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return [.. tools.Select(tool => Entry(tool, commandOf))];
    }

    /// <summary>The entries of the tools one class declares, built as the MCP SDK builds them and never invoked.</summary>
    /// <param name="toolType">A class whose methods carry <see cref="McpServerToolAttribute"/>.</param>
    /// <param name="services">The services the server registers, so the parameters they fill stay out of the schema as they do on the wire.</param>
    /// <param name="json">The server's JSON options (<see cref="ToolJson.Options"/>), which a native build needs for any type beyond the primitives.</param>
    /// <remarks>
    /// One class at a time, named with <c>typeof</c>: a native build keeps the methods of a type it is told about, and a
    /// list of types tells it nothing about each, so <c>[.. ToolCatalog.Of(typeof(A)), .. ToolCatalog.Of(typeof(B))]</c>.
    /// </remarks>
    public static IReadOnlyList<ToolEntry> Of(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type toolType,
        IServiceProvider? services = null,
        JsonSerializerOptions? json = null)
    {
        ArgumentNullException.ThrowIfNull(toolType);

        var options = new McpServerToolCreateOptions { Services = services, SerializerOptions = json };
        var declared = toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var tools = declared.Where(method => method.IsDefined(typeof(McpServerToolAttribute), inherit: false));

        return Of([.. tools.Select(method => Built(method, options))], commandOf: null);
    }

    /// <summary>The skill a tool belongs to: the name of the <see cref="SkillAttribute"/> on its class, or null.</summary>
    public static string? SkillOf(McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.Metadata.OfType<SkillAttribute>().FirstOrDefault()?.Name;
    }

    /// <summary>The parameters an input schema declares, in its order.</summary>
    internal static IReadOnlyList<ToolParameter> ParametersOf(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return [];

        var required = RequiredOf(schema);

        return [.. properties.EnumerateObject().Select(property => new ToolParameter(property.Name, TypeOf(property.Value), required.Contains(property.Name), DescriptionOf(property.Value)))];
    }

    /// <summary>The names an input schema requires.</summary>
    internal static IReadOnlyList<string> RequiredOf(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
            ? [.. required.EnumerateArray().Where(name => name.ValueKind == JsonValueKind.String).Select(name => name.GetString()!)]
            : [];

    private static ToolEntry Entry(McpServerTool tool, Func<string, string?>? commandOf)
    {
        var protocol = tool.ProtocolTool;

        return new ToolEntry(protocol.Name, protocol.Description ?? string.Empty, SkillOf(tool), commandOf?.Invoke(protocol.Name), ParametersOf(protocol.InputSchema));
    }

    private static McpServerTool Built(MethodInfo method, McpServerToolCreateOptions options) =>
        method.IsStatic ? McpServerTool.Create(method, target: null, options) : McpServerTool.Create(method, NeverCreated, options);

    private static object NeverCreated(RequestContext<CallToolRequestParams> request) =>
        throw new NotSupportedException("a catalog describes its tools and never invokes one");

    /// <summary>The JSON type of a property: its <c>type</c>, the first that is not null of several, a string for an enum, an object otherwise.</summary>
    private static string TypeOf(JsonElement property)
    {
        if (property.ValueKind != JsonValueKind.Object)
            return "object";

        if (property.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            return type.GetString()!;

        if (type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Select(each => each.GetString()).FirstOrDefault(each => each is not null and not "null") is { } first)
            return first;

        return property.TryGetProperty("enum", out _) ? "string" : "object";
    }

    private static string? DescriptionOf(JsonElement property) =>
        property.ValueKind == JsonValueKind.Object && property.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
            ? description.GetString()
            : null;
}

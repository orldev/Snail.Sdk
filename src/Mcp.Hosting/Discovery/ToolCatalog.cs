using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
/// <c>find_tool</c> reads the tools the server registered, which is what a server needs. <see cref="Of(IServiceProvider)"/>
/// reads the same tools from the server's services without a host — the ones Snail.Sdk adds among them —
/// <see cref="Of(Type, IServiceProvider?, JsonSerializerOptions?, Func{string, string?}?)"/> one class without a server, for a
/// guard test or a catalog of tools the server does not register, and
/// <see cref="Find(IServiceProvider, string?, int)"/> ranks them against a task as <c>find_tool</c> does, for a test of the
/// server's words.
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
    /// <param name="commandOf">What a sequence or a program calls a tool, by the tool's name — <see cref="DiscoveryOptions.CommandOf"/> — so an entry carries the command <c>find_tool</c> answers; none when null.</param>
    /// <remarks>
    /// <para>
    /// One class at a time, named with <c>typeof</c>: a native build keeps the methods of a type it is told about, and a
    /// list of types tells it nothing about each, so <c>[.. ToolCatalog.Of(typeof(A)), .. ToolCatalog.Of(typeof(B))]</c>.
    /// </para>
    /// <para>
    /// Without <paramref name="commandOf"/> a test set each entry's command itself, <c>entry with { Command = … }</c>, to
    /// see what <c>find_tool</c> answers (Snail.MCP.Git).
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ToolEntry> Of(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type toolType,
        IServiceProvider? services = null,
        JsonSerializerOptions? json = null,
        Func<string, string?>? commandOf = null)
    {
        ArgumentNullException.ThrowIfNull(toolType);

        var options = new McpServerToolCreateOptions { Services = services, SerializerOptions = json };
        var declared = toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var tools = declared.Where(method => method.IsDefined(typeof(McpServerToolAttribute), inherit: false));

        return Of([.. tools.Select(method => Built(method, options))], commandOf);
    }

    /// <summary>The catalog <c>find_tool</c> searches, read from a server's services without a host: every tool it registers, the ones Snail.Sdk adds among them.</summary>
    /// <remarks>
    /// <para>
    /// A catalog of the server's classes lacked what the <c>With…</c> registrations add — <c>find_tool</c>, <c>run_tools</c>,
    /// <c>run_program</c>, the skill tools — and the commands and parameter names <see cref="DiscoveryOptions"/> gives,
    /// so a test of the server's words ranked a list that was not the one <c>find_tool</c> ranks (Snail.MCP.Git). This
    /// reads the tool collection the services would hand a host — the descriptions the registrations fit included — with
    /// <see cref="DiscoveryOptions.CommandOf"/>, <see cref="DiscoveryOptions.ParameterOf"/> and
    /// <see cref="DiscoveryOptions.Catalog"/> applied as <c>find_tool</c> applies them. No host is built and no tool runs:
    /// <c>var services = new ServiceCollection(); services.AddMcpServer().WithTools&lt;GitTools&gt;().WithToolDiscovery(…);</c>
    /// and <c>ToolCatalog.Of(services.BuildServiceProvider())</c>.
    /// </para>
    /// <para>
    /// Every tool, the session's grant aside: <c>find_tool</c> leaves out what a session may not call, which is a fact
    /// about the session rather than about the catalog.
    /// </para>
    /// </remarks>
    /// <param name="services">The services the server's registrations built: <c>AddMcpServer()</c> and its <c>With…</c> calls.</param>
    public static IReadOnlyList<ToolEntry> Of(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var tools = services.GetService<IOptions<McpServerOptions>>()?.Value.ToolCollection is { } collection
            ? collection
            : services.GetServices<McpServerTool>();

        return Of(tools, services.GetService<DiscoveryOptions>() ?? new DiscoveryOptions());
    }

    /// <summary>The entries <c>find_tool</c> searches among the tools: the server's own catalog when it set one, the parameters of a command named as the command takes them.</summary>
    internal static IReadOnlyList<ToolEntry> Of(IEnumerable<McpServerTool> tools, DiscoveryOptions options)
    {
        var catalog = options.Catalog ?? Of(tools, options.CommandOf);

        return options.ParameterOf is { } parameterOf ? [.. catalog.Select(entry => Commanded(entry, parameterOf))] : catalog;
    }

    /// <summary>An entry with a command, its parameters renamed as the command takes them and the ones it does not take dropped.</summary>
    private static ToolEntry Commanded(ToolEntry entry, Func<string, string, string?> parameterOf)
    {
        if (entry.Command is null)
            return entry;

        var named = entry.Parameters.Select(parameter => (Parameter: parameter, Name: parameterOf(entry.Name, parameter.Name)));

        return entry with { Parameters = [.. named.Where(each => each.Name is not null).Select(each => each.Parameter with { Name = each.Name! })] };
    }

    /// <summary>The skill a tool belongs to: the name of the <see cref="SkillAttribute"/> on its class, or null.</summary>
    public static string? SkillOf(McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.Metadata.OfType<SkillAttribute>().FirstOrDefault()?.Name;
    }

    /// <summary>The entries that answer a task, best first and at most <paramref name="limit"/>: the ranking of <c>find_tool</c>, without a server.</summary>
    /// <remarks>
    /// <para>
    /// For a server's test of its own words: that "publish the branch" finds <c>git_push</c> first is a fact about its
    /// <see cref="DiscoveryOptions.Meanings"/> and descriptions, and asking it of <c>find_tool</c> took a host
    /// (Snail.MCP.Git). <c>find_tool</c> answers with this ranking, over the tools the session may call.
    /// </para>
    /// <para>
    /// Empty when the task holds no word that says something, or no entry answers it. Ties go by name, so an answer is
    /// the same on every run.
    /// </para>
    /// </remarks>
    /// <param name="entries">The catalog: <see cref="Of(IServiceProvider)"/>, <see cref="Of(IEnumerable{McpServerTool}, Func{string, string?})"/>, <see cref="Of(Type, IServiceProvider?, JsonSerializerOptions?, Func{string, string?}?)"/> or the server's own.</param>
    /// <param name="task">The task in words, as a model sends it.</param>
    /// <param name="limit">The most entries answered, one or more.</param>
    /// <param name="options">The server's prefix, noise and meanings, as <c>WithToolDiscovery</c> takes them; none when null.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is below one.</exception>
    public static IReadOnlyList<ToolEntry> Find(IReadOnlyList<ToolEntry> entries, string? task, int limit, DiscoveryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var words = options ?? new DiscoveryOptions();

        return Find(entries, task, limit, new Vocabulary(words.Noise, words.Prefix), words.Meanings);
    }

    /// <summary>The tools of a server that <c>find_tool</c> would name for a task, best first: its ranking over <see cref="Of(IServiceProvider)"/>, without a host.</summary>
    /// <remarks>
    /// The words are the server's <see cref="DiscoveryOptions"/> as <c>WithToolDiscovery</c> registered them, and the limit is
    /// held between one and twenty as <c>find_tool</c> holds it, so a test asks exactly what a model asking would be answered.
    /// </remarks>
    /// <param name="services">The services the server's registrations built.</param>
    /// <param name="task">The task in words, as a model sends it.</param>
    /// <param name="limit">The most entries answered, as <c>find_tool</c>'s <c>limit</c>.</param>
    public static IReadOnlyList<ToolEntry> Find(IServiceProvider services, string? task, int limit)
    {
        ArgumentNullException.ThrowIfNull(services);

        var words = services.GetService<DiscoveryOptions>() ?? new DiscoveryOptions();

        return Find(Of(services), task, Math.Clamp(limit, 1, Discovery.MostTools), new Vocabulary(words.Noise, words.Prefix), words.Meanings);
    }

    /// <summary>The entries that answer a task, scored by <paramref name="vocabulary"/>, best first and at most <paramref name="limit"/>.</summary>
    internal static IReadOnlyList<ToolEntry> Find(IReadOnlyList<ToolEntry> entries, string? task, int limit, Vocabulary vocabulary, IDictionary<string, string> meanings)
    {
        var asked = vocabulary.Asked(task);

        if (asked.Count == 0)
            return [];

        var answering = entries
            .Select(entry => (Entry: entry, Score: vocabulary.Score(asked, entry.Name, meanings.TryGetValue(entry.Name, out var meant) ? meant : null, entry.Description)))
            .Where(scored => scored.Score > 0);
        var best = answering
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Entry.Name, StringComparer.Ordinal)
            .Take(limit);

        return [.. best.Select(scored => scored.Entry)];
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

    /// <summary>The JSON type of a property: its <c>type</c>, the first that is not null of several, a string for an enum, an object otherwise; an array named by its items.</summary>
    /// <remarks>
    /// An array is <c>string[]</c> rather than <c>array</c>, and an array of arrays <c>integer[][]</c>: "array" told a model
    /// nothing about what to put in it, and Snail.MCP.CleanUp's own index had written <c>string[]</c> before the SDK's.
    /// An array whose items say nothing stays <c>array</c>.
    /// </remarks>
    private static string TypeOf(JsonElement property) => KindOf(property) ?? (property.ValueKind == JsonValueKind.Object && property.TryGetProperty("enum", out _) ? "string" : "object");

    /// <summary>The type a schema states, or null when it states none.</summary>
    private static string? KindOf(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type))
            return null;

        var kind = type.ValueKind switch
        {
            JsonValueKind.String => type.GetString(),
            JsonValueKind.Array => type.EnumerateArray().Select(each => each.ValueKind == JsonValueKind.String ? each.GetString() : null).FirstOrDefault(each => each is not null and not "null"),
            _ => null,
        };

        return kind == "array" ? ArrayOf(schema) : kind;
    }

    /// <summary>An array named by its items, <c>string[]</c>; <c>array</c> when the items say nothing of their type.</summary>
    private static string ArrayOf(JsonElement schema)
    {
        if (!schema.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
            return "array";

        var item = KindOf(items) ?? (items.TryGetProperty("enum", out _) ? "string" : null) ?? (items.TryGetProperty("properties", out _) ? "object" : null);

        return item is null ? "array" : $"{item}[]";
    }

    private static string? DescriptionOf(JsonElement property) =>
        property.ValueKind == JsonValueKind.Object && property.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
            ? description.GetString()
            : null;
}

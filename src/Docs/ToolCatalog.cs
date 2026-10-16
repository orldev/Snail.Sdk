using System.Reflection;
using System.Text.Json;

namespace Snail.Sdk.Docs;

/// <summary>One tool, prompt or resource as a client receives it: its name, its description, and the whole wire object.</summary>
internal sealed record CatalogEntry(string Name, string Description, JsonElement Wire);

/// <summary>Tools that share a declaring class and therefore a subject; skill tools carry the skill that loads them.</summary>
internal sealed record ToolFamily(string Type, string Slug, string Title, string? Skill, IReadOnlyList<CatalogEntry> Tools);

/// <summary>Everything a server declares, grouped the way its pages show it.</summary>
internal sealed record Catalog(IReadOnlyList<ToolFamily> Families, IReadOnlyList<CatalogEntry> Prompts, IReadOnlyList<CatalogEntry> Resources);

/// <summary>Reads a server's tools, prompts and resources through the server's own MCP SDK, never invoking one.</summary>
internal static class ToolCatalog
{
    private const string Server = "ModelContextProtocol.Server.";

    private static readonly Primitive ToolPrimitive = new("McpServerToolTypeAttribute", "McpServerToolAttribute", "McpServerTool", "McpServerToolCreateOptions", "ProtocolTool");
    private static readonly Primitive PromptPrimitive = new("McpServerPromptTypeAttribute", "McpServerPromptAttribute", "McpServerPrompt", "McpServerPromptCreateOptions", "ProtocolPrompt");
    private static readonly Primitive ResourcePrimitive = new("McpServerResourceTypeAttribute", "McpServerResourceAttribute", "McpServerResource", "McpServerResourceCreateOptions", "ProtocolResourceTemplate");

    /// <summary>The catalog of a loaded server, its families named and ordered by the settings.</summary>
    public static Catalog Read(ServerContext context, CatalogSettings settings)
    {
        var sdk = new Sdk(context);
        var declared = Declaring(context.Server, ToolPrimitive)
            .Select(type => (Type: type, Tools: sdk.Describe(type, ToolPrimitive)))
            .Where(declaration => declaration.Tools.Count > 0)
            .ToDictionary(declaration => declaration.Type.Name, StringComparer.Ordinal);

        var unnamed = settings.Families.Count == 0 ? [] : declared.Keys.Where(name => !settings.Names(name)).Order(StringComparer.Ordinal).ToList();
        var absent = settings.Families.Select(family => family.Type).Where(type => !declared.ContainsKey(type)).ToList();

        if (unnamed.Count > 0)
            throw new DocsError(DocsError.SettingsAndServerDisagree, $"these tool classes have no family in docs/tools.json: {string.Join(", ", unnamed)}");

        if (absent.Count > 0)
            throw new DocsError(DocsError.SettingsAndServerDisagree, $"docs/tools.json names families of classes the server does not declare tools in: {string.Join(", ", absent)}");

        var families = settings.Order(declared.Keys)
            .Select(name => settings.Family(name, Skill(declared[name].Type), declared[name].Tools))
            .ToList();
        var shared = families.GroupBy(family => family.Slug, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => $"{group.Key} ({string.Join(", ", group.Select(family => family.Type))})").ToList();

        if (shared.Count > 0)
            throw new DocsError(DocsError.SettingsAndServerDisagree, $"families would share a page: {string.Join("; ", shared)}; name them in docs/tools.json");
        var prompts = Declaring(context.Server, PromptPrimitive).SelectMany(type => sdk.Describe(type, PromptPrimitive)).ToList();
        var resources = Declaring(context.Server, ResourcePrimitive).SelectMany(type => sdk.Describe(type, ResourcePrimitive)).ToList();

        return new Catalog(families, prompts, resources);
    }

    private static IEnumerable<Type> Declaring(Assembly server, Primitive primitive) =>
        Loadable(server).Where(type => HasAttribute(type, primitive.TypeAttribute));

    private static IEnumerable<Type> Loadable(Assembly server)
    {
        try
        {
            return server.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }

    private static bool HasAttribute(MemberInfo member, string attributeName) =>
        member.GetCustomAttributesData().Any(attribute => attribute.AttributeType.FullName == $"{Server}{attributeName}");

    /// <summary>The skill a class of tools belongs to: the first argument of an attribute named SkillAttribute, whichever namespace holds it.</summary>
    private static string? Skill(Type type) =>
        type.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.Name == "SkillAttribute" && attribute.ConstructorArguments.Count > 0)
            .Select(attribute => attribute.ConstructorArguments[0].Value as string)
            .FirstOrDefault();

    /// <summary>The names of one kind of MCP primitive in the SDK: the attributes that declare it, its type, and the property holding its wire object.</summary>
    private sealed record Primitive(string TypeAttribute, string MethodAttribute, string Type, string Options, string Protocol);

    /// <summary>The server's MCP SDK, called by reflection: its types live in the server's load context, not in this program's.</summary>
    private sealed class Sdk(ServerContext context)
    {
        private readonly object _jsonOptions = context.SdkType("ModelContextProtocol.McpJsonUtilities")?.GetProperty("DefaultOptions")?.GetValue(null)
            ?? throw new InvalidOperationException("the server carries no ModelContextProtocol.Core: it is not an MCP server");

        public IReadOnlyList<CatalogEntry> Describe(Type type, Primitive primitive)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => HasAttribute(method, primitive.MethodAttribute))
                .OrderBy(method => method.MetadataToken);

            return [.. methods.Select(method => Entry(Wire(method, primitive)))];
        }

        private JsonElement Wire(MethodInfo method, Primitive primitive)
        {
            var sdkType = context.SdkType($"{Server}{primitive.Type}")!;
            var optionsType = context.SdkType($"{Server}{primitive.Options}")!;
            var options = Activator.CreateInstance(optionsType);

            optionsType.GetProperty("Services")?.SetValue(options, new ServiceGuess(context.Server));
            var created = method.IsStatic ? CreateStatic(sdkType, method, options) : CreateInstance(sdkType, method, options);
            var protocol = sdkType.GetProperty(primitive.Protocol)!.GetValue(created)!;

            return JsonDocument.Parse(Serialize(protocol)).RootElement.Clone();
        }

        private static object CreateStatic(Type sdkType, MethodInfo method, object? options)
        {
            var create = sdkType.GetMethods().Single(candidate => candidate.Name == "Create" && Parameters(candidate) is [_, { ParameterType.FullName: "System.Object" }, _]);

            return create.Invoke(null, [method, null, options])!;
        }

        private static object CreateInstance(Type sdkType, MethodInfo method, object? options)
        {
            var create = sdkType.GetMethods().Single(candidate => candidate.Name == "Create" && Parameters(candidate) is [{ ParameterType.Name: "MethodInfo" }, { ParameterType.IsGenericType: true }, _]);
            var factoryType = create.GetParameters()[1].ParameterType;
            var never = typeof(Sdk).GetMethod(nameof(Never), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(factoryType.GetGenericArguments()[0])
                .CreateDelegate(factoryType);

            return create.Invoke(null, [method, never, options])!;
        }

        private static ParameterInfo[] Parameters(MethodInfo method) => method.GetParameters();

        private static object Never<TContext>(TContext context) =>
            throw new NotSupportedException("the reference reads declarations only and never invokes a tool");

        /// <summary>The wire object as the server would send it: its own System.Text.Json with its own options.</summary>
        private string Serialize(object protocol)
        {
            var serializer = _jsonOptions.GetType().Assembly.GetType("System.Text.Json.JsonSerializer")!;
            var serialize = serializer.GetMethod("Serialize", [typeof(object), typeof(Type), _jsonOptions.GetType()])!;

            return (string)serialize.Invoke(null, [protocol, protocol.GetType(), _jsonOptions])!;
        }

        private static CatalogEntry Entry(JsonElement wire)
        {
            var name = wire.TryGetProperty("name", out var named) ? named.GetString() ?? string.Empty : string.Empty;
            var description = wire.TryGetProperty("description", out var described) ? described.GetString() ?? string.Empty : string.Empty;

            return new CatalogEntry(name, description, wire);
        }
    }
}

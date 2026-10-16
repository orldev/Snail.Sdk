using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

namespace Snail.Sdk.Docs;

/// <summary>A built server loaded beside this program, with the packages it was built with and not this program's.</summary>
/// <remarks>
/// The wire copy of a tool is what the server's own copy of the MCP SDK makes of the method, so the server is loaded with
/// every assembly from its output folder, and with the shared frameworks its runtimeconfig.json names beyond the one this
/// program runs on (ASP.NET Core, for a server with HTTP): whichever SDK version it uses describes its tools. Nothing the server declares
/// runs — no host is built, no tool invoked; the SDK only reads the methods. The context lives as long as this short
/// program: not collectible, since the container the descriptions are made with implements one of its interfaces, which
/// a generated proxy cannot do for a collectible assembly.
/// </remarks>
internal sealed class ServerContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string[] _folders;

    private ServerContext(string assemblyPath)
        : base($"Snail.Sdk.Docs {Path.GetFileNameWithoutExtension(assemblyPath)}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _folders = [Path.GetDirectoryName(assemblyPath)!, .. SharedFrameworks(assemblyPath)];
    }

    /// <summary>The server's assembly, loaded in a context of its own.</summary>
    public Assembly Server { get; private set; } = null!;

    /// <summary>Loads the server from its output folder.</summary>
    public static ServerContext Open(string assemblyPath)
    {
        var fullPath = Path.GetFullPath(assemblyPath);
        var context = new ServerContext(fullPath);

        context.Server = context.LoadFromAssemblyPath(fullPath);

        return context;
    }

    /// <summary>A type of the server's own MCP SDK, ModelContextProtocol.Core, found by its full name.</summary>
    public Type? SdkType(string fullName)
    {
        var core = new AssemblyName("ModelContextProtocol.Core");
        var assembly = Assemblies.FirstOrDefault(loaded => loaded.GetName().Name == core.Name) ?? Load(core);

        return assembly?.GetType(fullName, throwOnError: false);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = _resolver.ResolveAssemblyToPath(assemblyName)
            ?? _folders.Select(folder => Path.Combine(folder, $"{assemblyName.Name}.dll")).FirstOrDefault(File.Exists);

        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <summary>
    /// The folders of the shared frameworks the server names beyond the one this program runs on, each at the newest
    /// version installed beside this runtime. Microsoft.NETCore.App comes from the runtime itself: a second copy loaded
    /// here would make IServiceProvider and its kind two different types on either side.
    /// </summary>
    private static IEnumerable<string> SharedFrameworks(string assemblyPath)
    {
        var settings = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");

        if (!File.Exists(settings))
            return [];

        using var document = JsonDocument.Parse(File.ReadAllText(settings));
        var shared = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", ".."));
        var names = FrameworkNames(document.RootElement.GetProperty("runtimeOptions")).Where(name => name != "Microsoft.NETCore.App").ToList();

        return [.. names.Select(name => Newest(Path.Combine(shared, name))).OfType<string>()];
    }

    private static IEnumerable<string> FrameworkNames(JsonElement options)
    {
        if (options.TryGetProperty("framework", out var single))
            yield return single.GetProperty("name").GetString()!;

        if (!options.TryGetProperty("frameworks", out var several))
            yield break;

        foreach (var framework in several.EnumerateArray())
            yield return framework.GetProperty("name").GetString()!;
    }

    private static string? Newest(string framework) =>
        Directory.Exists(framework)
            ? Directory.EnumerateDirectories(framework).MaxBy(version => Version.TryParse(Path.GetFileName(version).Split('-')[0], out var parsed) ? parsed : new Version())
            : null;
}

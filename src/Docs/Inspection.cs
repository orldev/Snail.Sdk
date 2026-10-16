using System.Reflection;
using System.Runtime.InteropServices;

namespace Snail.Sdk.Docs;

/// <summary>A built assembly opened for reading only, with every assembly it was compiled against at hand.</summary>
/// <remarks>
/// MetadataLoadContext never runs the assembly's code, so a generator can read a server or a library that would not even
/// start here. It resolves a type only from the assemblies it is given: the build passes its references, and the runtime
/// directory covers the framework.
/// </remarks>
internal sealed class Inspection : IDisposable
{
    private readonly MetadataLoadContext _context;

    private Inspection(MetadataLoadContext context, Assembly assembly)
    {
        _context = context;
        Assembly = assembly;
    }

    /// <summary>The assembly under inspection.</summary>
    public Assembly Assembly { get; }

    /// <summary>Opens the assembly with its references and the runtime's own assemblies.</summary>
    public static Inspection Open(string assemblyPath, IEnumerable<string> references)
    {
        var runtime = Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");
        var present = references.Where(File.Exists);
        var candidates = present.Concat(runtime).Append(assemblyPath);
        var byFileName = candidates.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        var paths = byFileName.Select(group => group.First());

        var context = new MetadataLoadContext(new PathAssemblyResolver(paths));
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

        return new Inspection(context, assembly);
    }

    /// <summary>The types a reader of the API sees, those the inspection could resolve.</summary>
    public IReadOnlyList<Type> VisibleTypes()
    {
        Type[] types;

        try
        {
            types = Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            types = [.. partial.Types.OfType<Type>()];
        }

        return [.. types.Where(type => type.IsVisible && !type.Name.Contains('<', StringComparison.Ordinal))];
    }

    public void Dispose() => _context.Dispose();
}

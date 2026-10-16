using System.Reflection;

namespace Snail.Sdk.Docs;

/// <summary>
/// The container a tool's description is made with: it tells the parameters a server fills from its services apart
/// from the arguments a client sends, so the reference shows what tools/list shows.
/// </summary>
/// <remarks>
/// The MCP SDK leaves a parameter out of the schema when the container's <c>IServiceProviderIsService</c> says its type
/// is a service. The real container is built by the server's host, which the reference never runs, so the answer is
/// read from the type: of .NET and ASP.NET, what a host registers (Microsoft.Extensions.*, Microsoft.AspNetCore.*,
/// TimeProvider); of the server's own, an interface or abstract class that is not a collection (a port), or a class that
/// is neither a record nor made by a parameterless constructor (a store, a registry). Values, strings, enumerations,
/// collections, JSON nodes and data types stay arguments.
/// </remarks>
internal sealed class ServiceGuess(Assembly server) : IServiceProvider
{
    private const string IsServiceContract = "Microsoft.Extensions.DependencyInjection.IServiceProviderIsService";

    private object? _isService;

    public object? GetService(Type serviceType) =>
        serviceType.FullName == IsServiceContract ? _isService ??= Answering(serviceType) : null;

    /// <summary>Whether a parameter of this type is one the server's container fills.</summary>
    public static bool IsService(Type type, Assembly server)
    {
        if (type.IsValueType || type.IsArray || type == typeof(string) || type.FullName == "System.Object")
            return false;

        if (IsFramework(type.Assembly))
            return IsFrameworkService(type);

        if (type.IsInterface || type.IsAbstract)
            return !IsCollection(type);

        var isOwn = type.Assembly == server || !IsFramework(type.Assembly);
        var isRecord = type.GetMethod("<Clone>$") is not null;
        var hasDefaultConstructor = type.GetConstructor(Type.EmptyTypes) is not null;

        return isOwn && !isRecord && !hasDefaultConstructor;
    }

    private object Answering(Type contract)
    {
        var proxy = DispatchProxy.Create(contract, typeof(IsServiceAnswer));

        ((IsServiceAnswer)proxy).Server = server;

        return proxy;
    }

    /// <summary>A type of .NET or ASP.NET is a service when it is one a host registers — a logger, options, a clock — and data otherwise (a JSON node, a stream).</summary>
    private static bool IsFrameworkService(Type type) =>
        type.Namespace?.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true
        || type.FullName == "System.TimeProvider";

    private static bool IsCollection(Type type) =>
        type.Namespace?.StartsWith("System.Collections", StringComparison.Ordinal) == true
        || type.GetInterfaces().Any(contract => contract.FullName == "System.Collections.IEnumerable");

    private static bool IsFramework(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? string.Empty;

        return name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal) || name == "netstandard";
    }
}

/// <summary>The server's <c>IServiceProviderIsService</c>, answered by <see cref="ServiceGuess.IsService"/>.</summary>
/// <remarks>Public and not sealed: DispatchProxy derives the proxy it generates from it.</remarks>
public class IsServiceAnswer : DispatchProxy
{
    internal Assembly? Server { get; set; }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        args is [Type type, ..] && Server is { } server && ServiceGuess.IsService(type, server);
}

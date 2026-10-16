using System.Reflection;
using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The name and version a server announces in <c>initialize</c>, read from its assembly so they are written once.</summary>
/// <remarks>
/// Snail.Sdk.Mcp writes the server's command, <c>s3l-mcp-&lt;name&gt;</c>, into the server's assembly as the metadata
/// <c>Snail.Sdk.Mcp.ServerName</c>, so a client knows the server by the same name a person types to start it. A server
/// built without Snail.Sdk.Mcp announces its assembly's name.
/// </remarks>
public static class ServerIdentity
{
    /// <summary>The assembly metadata key Snail.Sdk.Mcp writes the server's command under; its targets name it by this string.</summary>
    internal const string NameKey = "Snail.Sdk.Mcp.ServerName";

    /// <summary>The name and version of the server built into this assembly.</summary>
    public static Implementation Of(Assembly server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new Implementation { Name = NameOf(server), Version = VersionOf(server) };
    }

    /// <summary>The server's command, <c>s3l-mcp-&lt;name&gt;</c>; the assembly's name for a server built otherwise.</summary>
    public static string NameOf(Assembly server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var written = server.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(metadata => metadata.Key == NameKey)?.Value;

        return string.IsNullOrWhiteSpace(written) ? server.GetName().Name ?? "server" : written;
    }

    /// <summary>The version of the build that is running, without the commit a build may append.</summary>
    public static string VersionOf(Assembly server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var informational = server.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        return string.IsNullOrWhiteSpace(informational) ? "0.0.0" : informational.Split('+')[0];
    }
}

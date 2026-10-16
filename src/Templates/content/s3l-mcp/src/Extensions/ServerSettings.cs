using Microsoft.Extensions.Configuration;
using Hosting = Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Template.Extensions;

/// <summary>
/// The settings of the server as every Snail server reads them: its settings file, then its SNAIL_MCP_TMPLSHORT_
/// variables, which win (Snail.Sdk.Mcp.Hosting.ServerSettings). Read before a host exists, since the transport is
/// decided from them; the hosts add the same sources to the configuration they build.
/// </summary>
public static class ServerSettings
{
    /// <summary>The server's short name: its command is s3l-mcp-tmplshort, its variables SNAIL_MCP_TMPLSHORT_.</summary>
    public const string Name = "tmplshort";

    private static readonly IConfigurationRoot Read = Hosting.ServerSettings.Read(Name);

#if (IsBoth)
    /// <summary>Whether Transport (SNAIL_MCP_TMPLSHORT_TRANSPORT) asks for HTTP rather than stdio.</summary>
    public static bool IsHttp => string.Equals(Read["Transport"], "http", StringComparison.OrdinalIgnoreCase);

#endif
    /// <summary>Where the HTTP endpoint listens: Url (SNAIL_MCP_TMPLSHORT_URL), a loopback address.</summary>
    public static Uri Url => new(Read["Url"] ?? "http://127.0.0.1:9300/");
}

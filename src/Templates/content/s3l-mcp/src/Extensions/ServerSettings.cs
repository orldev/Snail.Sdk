using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Hosting;
using Hosting = Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Template.Extensions;

/// <summary>
/// The settings of the server as every Snail server reads them: its settings file, then its SNAIL_MCP_TMPLSHORT_
/// variables, which win (Snail.Sdk.Mcp.Hosting.ServerSettings). Read once, before a host exists, since the transport
/// is decided from them; either host is built over them rather than reading them again.
/// </summary>
public static class ServerSettings
{
    /// <summary>The server's short name: its command is s3l-mcp-tmplshort, its variables SNAIL_MCP_TMPLSHORT_.</summary>
    public const string Name = "tmplshort";

    /// <summary>Where the HTTP endpoint listens unless Url (SNAIL_MCP_TMPLSHORT_URL) or Port says otherwise.</summary>
    private static readonly Uri DefaultUrl = new("http://127.0.0.1:9300/");

    /// <summary>The settings as the server read them at its start: the configuration of either host.</summary>
    public static IConfigurationRoot Values { get; } = Hosting.ServerSettings.Read(Name);

#if (IsBoth)
    /// <summary>
    /// Where clients reach the server: stdio, or HTTP when Transport (SNAIL_MCP_TMPLSHORT_TRANSPORT) says Http or a
    /// Port is set — on loopback, or on every interface inside a container (McpEndpoint.Read).
    /// </summary>
    public static McpEndpoint Endpoint => McpEndpoint.Read(Values, DefaultUrl);
#else
    /// <summary>
    /// Where clients reach the server, which is always HTTP: Url (SNAIL_MCP_TMPLSHORT_URL) or a Port, on loopback, or on
    /// every interface inside a container (McpEndpoint.ReadHttp, which refuses a Transport of Stdio at the start).
    /// </summary>
    public static McpEndpoint Endpoint => McpEndpoint.ReadHttp(Values, DefaultUrl);
#endif
}

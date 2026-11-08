using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Hosting;
using Hosting = Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Template.Extensions;

/// <summary>The settings of the server as every Snail server reads them: its settings file, then its SNAIL_MCP_TMPLSHORT_ variables, which win.</summary>
/// <remarks>
/// Read once (Snail.Sdk.Mcp.Hosting.ServerSettings), before a host exists, since the transport is decided from them;
/// either host is built over them rather than reading them again.
/// </remarks>
public static class ServerSettings
{
    /// <summary>The server's short name: its command is s3l-mcp-tmplshort, its variables SNAIL_MCP_TMPLSHORT_.</summary>
    public const string Name = "tmplshort";

    /// <summary>Where the HTTP endpoint listens unless Url (SNAIL_MCP_TMPLSHORT_URL) or Port says otherwise.</summary>
    private static readonly Uri DefaultUrl = new("http://127.0.0.1:9300/");

    /// <summary>How the endpoint is read: on loopback on this machine and every interface inside the image, its variables spelled by the server's name.</summary>
    /// <remarks>
    /// The endpoint asks for no key, so the image's published port is its only boundary; a setting that would open it
    /// beyond stops the server at its start, naming the variable. A server that puts a key in front of its endpoint
    /// reads it with McpReach.Unrestricted. The name spells the variable in the refusal under a test runner too, whose
    /// assembly names no server.
    /// </remarks>
    private static readonly McpEndpointOptions EndpointOptions = new() { Reach = McpReach.LoopbackOrContainer, Server = Name };

    /// <summary>The settings as the server read them at its start: the configuration of either host.</summary>
    public static IConfigurationRoot Values { get; } = Hosting.ServerSettings.Read(Name);

#if (IsBoth)
    /// <summary>Where clients reach the server: stdio, or HTTP when Transport (SNAIL_MCP_TMPLSHORT_TRANSPORT) says Http or a Port is set.</summary>
    /// <remarks>On loopback, or on every interface inside a container (McpEndpoint.Read).</remarks>
    public static McpEndpoint Endpoint => McpEndpoint.Read(Values, DefaultUrl, EndpointOptions);
#else
    /// <summary>Where clients reach the server, which is always HTTP: Url (SNAIL_MCP_TMPLSHORT_URL) or a Port.</summary>
    /// <remarks>On loopback, or on every interface inside a container (McpEndpoint.ReadHttp, which refuses a Transport of Stdio at the start).</remarks>
    public static McpEndpoint Endpoint => McpEndpoint.ReadHttp(Values, DefaultUrl, EndpointOptions);
#endif
}

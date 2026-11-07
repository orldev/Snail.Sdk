using System.Globalization;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>Where a server answers: over stdio, or over HTTP at an address, on loopback or on every interface.</summary>
/// <param name="Transport">Stdio, or HTTP at <paramref name="Url"/>.</param>
/// <param name="Url">The address HTTP listens on; its port alone counts when the server listens on every interface.</param>
/// <param name="ListensOnEveryInterface">Whether the port is opened on every interface rather than on loopback alone.</param>
public sealed record McpEndpoint(McpTransport Transport, Uri Url, bool ListensOnEveryInterface)
{
    /// <summary>The variable the .NET container images set to <c>true</c>.</summary>
    internal const string ContainerVariable = "DOTNET_RUNNING_IN_CONTAINER";

    /// <summary>The endpoint a server's settings ask for, in either of the two ways the servers of the fleet say it.</summary>
    /// <remarks>
    /// <para>
    /// <c>Transport</c> (<c>Stdio</c> or <c>Http</c>) with <c>Url</c>, or <c>Http:Url</c> when there is no <c>Url</c>; or a
    /// <c>Port</c> above zero, which alone means HTTP on that port of <paramref name="defaultUrl"/>'s host. A
    /// <c>Transport</c> that is set decides even beside a <c>Port</c>, and a <c>Port</c> replaces the port of the URL.
    /// The keys are read at the root of <paramref name="settings"/> — <c>SNAIL_MCP_&lt;NAME&gt;_TRANSPORT</c>,
    /// <c>_URL</c>, <c>_HTTP__URL</c>, <c>_PORT</c> and <c>_LISTENS_ON_EVERY_INTERFACE</c> — so a server that keeps them
    /// in a section passes that section.
    /// </para>
    /// <para>
    /// The port is opened on every interface when <c>ListensOnEveryInterface</c> is true, when the URL names the
    /// unspecified address (<c>0.0.0.0</c>, <c>[::]</c>), which means every interface, and inside a container
    /// (<c>DOTNET_RUNNING_IN_CONTAINER=true</c>): a container's published port does not reach its loopback, and the
    /// published port is the boundary. Anywhere else it stays on loopback, since the endpoint asks for no key.
    /// </para>
    /// <para>
    /// Settings that name neither a transport nor a port get <paramref name="defaultTransport"/>: stdio for a server that
    /// answers both ways, HTTP for one that answers only over HTTP — which, without it, came out a stdio endpoint no HTTP
    /// builder could take, and had to say <c>with { Transport = McpTransport.Http }</c> (the s3l-mcp template, 0.5.0).
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">A key holds what it cannot mean: a transport not named above, a URL that is not an absolute http one, a port or a flag that is not one.</exception>
    public static McpEndpoint Read(IConfiguration settings, Uri defaultUrl, McpTransport defaultTransport = McpTransport.Stdio) =>
        Read(settings, defaultUrl, Environment.GetEnvironmentVariable(ContainerVariable), defaultTransport);

    /// <summary>The endpoint the settings ask for, with the container variable as given.</summary>
    internal static McpEndpoint Read(IConfiguration settings, Uri defaultUrl, string? containerVariable, McpTransport defaultTransport = McpTransport.Stdio)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(defaultUrl);

        var transport = TransportOf(settings["Transport"]);
        var port = PortOf(settings["Port"]);
        var url = UrlOf(settings["Url"] ?? settings["Http:Url"], defaultUrl);

        if (port > 0)
            url = new UriBuilder(url) { Port = port }.Uri;

        var chosen = transport ?? (port > 0 ? McpTransport.Http : defaultTransport);
        var isInContainer = string.Equals(containerVariable?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        var listensOnEveryInterface = IsTrue(settings["ListensOnEveryInterface"]) || isInContainer || IsUnspecified(url);

        return new McpEndpoint(chosen, url, listensOnEveryInterface);
    }

    private static McpTransport? TransportOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Enum.TryParse<McpTransport>(value.Trim(), ignoreCase: true, out var transport) && Enum.IsDefined(transport))
            return transport;

        throw new InvalidOperationException($"Transport is '{value}'; it is Stdio or Http.");
    }

    private static int PortOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port <= ushort.MaxValue)
            return port;

        throw new InvalidOperationException($"Port is '{value}'; it is a port number, or 0 for stdio.");
    }

    private static Uri UrlOf(string? value, Uri defaultUrl)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultUrl;

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps))
            return url;

        throw new InvalidOperationException($"Url is '{value}'; it is an absolute http address, such as {defaultUrl}.");
    }

    private static bool IsTrue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value.Trim(), out var flag))
            return flag;

        throw new InvalidOperationException($"ListensOnEveryInterface is '{value}'; it is true or false.");
    }

    private static bool IsUnspecified(Uri url) =>
        IPAddress.TryParse(url.DnsSafeHost, out var address) && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));
}

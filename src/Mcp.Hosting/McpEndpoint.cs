using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
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

    /// <summary>The endpoint the settings of a server of both transports ask for, in either of the two ways the servers of the fleet say it; stdio when they name neither a transport nor a port.</summary>
    /// <remarks>
    /// <para>
    /// <c>Transport</c> (<c>Stdio</c> or <c>Http</c>) with <c>Url</c>, or <c>Http:Url</c> when there is no <c>Url</c>; or a
    /// <c>Port</c> above zero, which alone means HTTP on that port of <paramref name="defaultUrl"/>'s host. A
    /// <c>Transport</c> that is set decides even beside a <c>Port</c>, and a <c>Port</c> replaces the port of the URL. A
    /// <c>Url</c> alone does not mean HTTP: it says where HTTP would listen. The keys are read at the root of
    /// <paramref name="settings"/> — <c>SNAIL_MCP_&lt;NAME&gt;_TRANSPORT</c>, <c>_URL</c>, <c>_HTTP__URL</c>, <c>_PORT</c> and
    /// <c>_LISTENS_ON_EVERY_INTERFACE</c> — so a server that keeps them in a section passes that section.
    /// </para>
    /// <para>
    /// The port is opened on every interface when <c>ListensOnEveryInterface</c> is true, when the URL names the
    /// unspecified address (<c>0.0.0.0</c>, <c>[::]</c>), which means every interface, and inside a container
    /// (<c>DOTNET_RUNNING_IN_CONTAINER=true</c>): a container's published port does not reach its loopback, and the
    /// published port is the boundary. Anywhere else it stays on loopback, since the endpoint asks for no key. A server
    /// that listens on every interface by default, outside a container too — Snail.MCP.E2E under launchd — names
    /// <c>0.0.0.0</c> in <paramref name="defaultUrl"/>: a <c>Port</c> keeps that host, and a loopback <c>Url</c> narrows it.
    /// </para>
    /// <para>
    /// A refusal names what the operator set: the variable, <c>SNAIL_MCP_&lt;NAME&gt;_URL</c>, and the key of the settings
    /// file beside it, for a program built on Snail.Sdk.Mcp, which names its server in its assembly
    /// (<see cref="ServerSettings.NameOf(Assembly?)"/>); the key alone for any other program.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">A key holds what it cannot mean: a transport not named above, a URL that is not an absolute http one, a port or a flag that is not one.</exception>
    public static McpEndpoint Read(IConfiguration settings, Uri defaultUrl) =>
        Read(settings, defaultUrl, isHttpOnly: false, Environment.GetEnvironmentVariable(ContainerVariable), ServerSettings.NameOf(Assembly.GetEntryAssembly()));

    /// <summary>The endpoint the settings of a server that answers only over HTTP ask for: HTTP whatever they name, and a transport or a port that would mean stdio refused.</summary>
    /// <remarks>
    /// The same keys and the same interfaces as <see cref="Read(IConfiguration, Uri)"/>. <c>Transport=Stdio</c> and
    /// <c>Port=0</c> are refused here, at the start, naming the variable: read as stdio, they came out an endpoint no HTTP
    /// builder could take, and the server stopped later with an <see cref="ArgumentException"/> that named neither
    /// (Snail.MCP.Sandbox, 0.5.0).
    /// </remarks>
    /// <exception cref="InvalidOperationException">A key holds what it cannot mean for an HTTP server: a transport other than Http, a URL that is not an absolute http one, a port that is not one above zero, a flag that is not one.</exception>
    public static McpEndpoint ReadHttp(IConfiguration settings, Uri defaultUrl) =>
        Read(settings, defaultUrl, isHttpOnly: true, Environment.GetEnvironmentVariable(ContainerVariable), ServerSettings.NameOf(Assembly.GetEntryAssembly()));

    /// <summary>The endpoint the settings ask for, with the container variable and the server's name as given.</summary>
    internal static McpEndpoint Read(IConfiguration settings, Uri defaultUrl, bool isHttpOnly, string? containerVariable, string? server)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(defaultUrl);

        var names = new SettingNames(settings, server);
        var urlKey = settings["Url"] is null ? "Http:Url" : "Url";

        var transport = TransportOf(settings["Transport"], isHttpOnly, names);
        var port = PortOf(settings["Port"], isHttpOnly, names);
        var url = UrlOf(settings[urlKey], defaultUrl, names.Of(urlKey));

        if (port > 0)
            url = new UriBuilder(url) { Port = port }.Uri;

        var chosen = transport ?? (port > 0 || isHttpOnly ? McpTransport.Http : McpTransport.Stdio);
        var isInContainer = string.Equals(containerVariable?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        var listensOnEveryInterface = IsTrue(settings["ListensOnEveryInterface"], names) || isInContainer || IsUnspecified(url);

        return new McpEndpoint(chosen, url, listensOnEveryInterface);
    }

    /// <summary>A key's name as a refusal says it: <c>SNAIL_MCP_DEMO_HTTP__URL</c> beside <c>Http:Url</c>, for a key at <c>Http:Url</c> of the demo server.</summary>
    internal static string VariableOf(string server, string path)
    {
        var sections = path.Split(':').Select(ScreamingSnake);

        return $"{ServerSettings.PrefixOf(server)}{string.Join("__", sections)}";
    }

    private static McpTransport? TransportOf(string? value, bool isHttpOnly, SettingNames names)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var isTransport = Enum.TryParse<McpTransport>(value.Trim(), ignoreCase: true, out var transport) && Enum.IsDefined(transport);

        if (isTransport && (!isHttpOnly || transport is McpTransport.Http))
            return transport;

        throw new InvalidOperationException(isHttpOnly
            ? $"{names.Of("Transport")} is '{value}'; the server answers only over HTTP, so it is Http, or unset."
            : $"{names.Of("Transport")} is '{value}'; it is Stdio or Http.");
    }

    private static int PortOf(string? value, bool isHttpOnly, SettingNames names)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var isPort = int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is >= 0 and <= ushort.MaxValue;

        if (isPort && (!isHttpOnly || port > 0))
            return port;

        throw new InvalidOperationException(isHttpOnly
            ? $"{names.Of("Port")} is '{value}'; the server answers only over HTTP, so it is a port number above zero, or unset."
            : $"{names.Of("Port")} is '{value}'; it is a port number, or 0 for stdio.");
    }

    private static Uri UrlOf(string? value, Uri defaultUrl, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultUrl;

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp)
            return url;

        throw new InvalidOperationException($"{name} is '{value}'; it is an absolute http address, such as {defaultUrl}.");
    }

    private static bool IsTrue(string? value, SettingNames names)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value.Trim(), out var flag))
            return flag;

        throw new InvalidOperationException($"{names.Of("ListensOnEveryInterface")} is '{value}'; it is true or false.");
    }

    private static bool IsUnspecified(Uri url) =>
        IPAddress.TryParse(url.DnsSafeHost, out var address) && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any));

    /// <summary><c>ListensOnEveryInterface</c> as a variable spells it: <c>LISTENS_ON_EVERY_INTERFACE</c>.</summary>
    private static string ScreamingSnake(string section)
    {
        var spelled = new StringBuilder(section.Length + 8);

        for (var index = 0; index < section.Length; index++)
        {
            var isWordStart = index > 0 && char.IsUpper(section[index]) && char.IsLower(section[index - 1]);

            if (isWordStart)
                spelled.Append('_');

            spelled.Append(char.ToUpperInvariant(section[index]));
        }

        return spelled.ToString();
    }

    /// <summary>How a refusal names a key of these settings: by the server's variable and the key of its settings file, or by the key alone.</summary>
    private sealed class SettingNames(IConfiguration settings, string? server)
    {
        public string Of(string key)
        {
            var path = settings is IConfigurationSection section ? $"{section.Path}:{key}" : key;

            return server is null ? path : $"{VariableOf(server, path)} (or {path} in the settings file)";
        }
    }
}

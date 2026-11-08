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

    /// <summary>The key of the settings <see cref="Url"/> was read from — <c>Url</c>, <c>Http:Url</c>, or either under the fallback section — as the settings file spells it; null when it is the server's default address.</summary>
    /// <remarks>
    /// <c>Url</c> at the root wins over <c>Http:Url</c>, so an operator's <c>SNAIL_MCP_&lt;NAME&gt;_URL</c> moves the listener of
    /// a server that keeps its address under <c>Http</c>. A server that signs links with its address signs them with
    /// <see cref="Url"/>, where it listens, and names this key in what it says about the address: Snail.MCP.Blender signed
    /// with <c>Http:Url</c> while <c>SNAIL_MCP_BLENDER_URL</c> had moved the listener, and its links led nowhere. A
    /// <c>Port</c> replaces the port of the address read from this key.
    /// </remarks>
    public string? UrlSetting { get; init; }

    /// <summary>The endpoint the settings of a server of both transports ask for, in either of the two ways the servers of the fleet say it; stdio when they name neither a transport nor a port.</summary>
    /// <remarks>
    /// <para>
    /// <c>Transport</c> (<c>Stdio</c> or <c>Http</c>) with <c>Url</c>, or <c>Http:Url</c> when there is no <c>Url</c>; or a
    /// <c>Port</c> above zero, which alone means HTTP on that port of <paramref name="defaultUrl"/>'s host. A
    /// <c>Transport</c> that is set decides even beside a <c>Port</c>, and a <c>Port</c> replaces the port of the URL. A
    /// <c>Url</c> alone does not mean HTTP: it says where HTTP would listen. The keys are read at the root of
    /// <paramref name="settings"/> — <c>SNAIL_MCP_&lt;NAME&gt;_TRANSPORT</c>, <c>_URL</c>, <c>_HTTP__URL</c>, <c>_PORT</c> and
    /// <c>_LISTENS_ON_EVERY_INTERFACE</c> — so a server that keeps them in a section passes that section, and one whose
    /// operators may write them at the root or in a section reads them with <see cref="Read(IConfiguration, Uri, string)"/>.
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
    /// (<see cref="ServerSettings.NameOf(Assembly?)"/>), or for a server that names itself in
    /// <see cref="McpEndpointOptions.Server"/>; the key alone for any other program. A refusal is a
    /// <see cref="StartRefusedException"/>, which the program catches to say it in one line and exit with 1.
    /// </para>
    /// <para>
    /// An endpoint over HTTP that listens on its own address listens on an IP address or <c>localhost</c>, so a URL at a
    /// host name is refused here: it was served on loopback without a word. An IP address beyond loopback, a LAN one,
    /// is refused on the machine, as every interface is: the reach of a read without options is
    /// <see cref="McpReach.LoopbackOrContainer"/>, and a server that guards its endpoint itself widens it with
    /// <see cref="Read(IConfiguration, Uri, McpEndpointOptions)"/> and <see cref="McpReach.Unrestricted"/>.
    /// </para>
    /// <para>
    /// Over stdio no port is opened, so <c>ListensOnEveryInterface=true</c> means nothing there, and is refused naming the
    /// variable rather than ignored: an operator who set it meant HTTP and got stdio without a word, and
    /// Snail.MCP.FileSystem and Git refused it themselves. Only the setting counts — the flag a container turns on is not
    /// the operator's — and a <c>Url</c> stays allowed, since it says where HTTP would listen once a transport or a port
    /// asks for it.
    /// </para>
    /// </remarks>
    /// <exception cref="StartRefusedException">A key holds what it cannot mean: a transport not named above, a URL that is not an absolute http one or names a host rather than an address, a port or a flag that is not one, or <c>ListensOnEveryInterface=true</c> over stdio.</exception>
    public static McpEndpoint Read(IConfiguration settings, Uri defaultUrl) =>
        Read(settings, defaultUrl, isHttpOnly: false, Environment.GetEnvironmentVariable(ContainerVariable), ServerSettings.NameOf(Assembly.GetEntryAssembly()));

    /// <summary>The endpoint of a server of both transports whose settings may keep its keys in a section as well as at the root.</summary>
    /// <param name="settings">The server's settings, read at their root first.</param>
    /// <param name="defaultUrl">Where HTTP listens when the settings name no address.</param>
    /// <param name="fallbackSection">The section a key is looked for in when the root does not hold it, such as <c>Snail</c>.</param>
    /// <remarks>
    /// The keys and their meaning are those of <see cref="Read(IConfiguration, Uri)"/>. Each key is looked for at the root
    /// first and in the section only when the root does not hold it, key by key: a <c>Port</c> at the root and a
    /// <c>Url</c> in the section are both read. Snail.MCP.Playwright, FileSystem, Documents and Git keep their other
    /// settings in a <c>Snail</c> section of the settings file, and their operators wrote the endpoint there too, where a
    /// read at the root alone did not look. A refusal names the key where it was found:
    /// <c>SNAIL_MCP_&lt;NAME&gt;_SNAIL__PORT (or Snail:Port in the settings file)</c> for a port in the section.
    /// </remarks>
    /// <exception cref="StartRefusedException">A key holds what it cannot mean: a transport not named above, a URL that is not an absolute http one, a port or a flag that is not one, or <c>ListensOnEveryInterface=true</c> over stdio.</exception>
    /// <exception cref="ArgumentException">The section's name is empty or blank.</exception>
    public static McpEndpoint Read(IConfiguration settings, Uri defaultUrl, string fallbackSection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackSection);

        return Read(settings, defaultUrl, isHttpOnly: false, Environment.GetEnvironmentVariable(ContainerVariable), ServerSettings.NameOf(Assembly.GetEntryAssembly()), fallbackSection);
    }

    /// <summary>The endpoint of a server of both transports, read as <paramref name="options"/> say: the section also looked in, and how far beyond loopback the server may listen.</summary>
    /// <param name="settings">The server's settings, read at their root first.</param>
    /// <param name="defaultUrl">Where HTTP listens when the settings name no address.</param>
    /// <param name="options">The fallback section, as <see cref="Read(IConfiguration, Uri, string)"/> takes it, the server's <see cref="McpReach"/>, and the name its variables are spelled by.</param>
    /// <remarks>
    /// <para>
    /// The keys and their meaning are those of <see cref="Read(IConfiguration, Uri)"/>. A server whose endpoint asks for no
    /// key and ships no image passes <c>new McpEndpointOptions { Reach = McpReach.LoopbackOnly }</c>, one that ships an
    /// image keeps <see cref="McpReach.LoopbackOrContainer"/>, the default, and one that guards its endpoint itself — a
    /// token of its own — passes <see cref="McpReach.Unrestricted"/>: an endpoint beyond that reach is refused here, at the start, naming what the operator
    /// set — <c>SNAIL_MCP_MEMORY_LISTENS_ON_EVERY_INTERFACE (or ListensOnEveryInterface in the settings file) is 'true'; …</c>,
    /// <c>DOTNET_RUNNING_IN_CONTAINER is 'true'; …</c> — rather than served to the network.
    /// </para>
    /// </remarks>
    /// <exception cref="StartRefusedException">A key holds what it cannot mean, as for <see cref="Read(IConfiguration, Uri)"/> — <c>ListensOnEveryInterface=true</c> over stdio among them — or an endpoint over HTTP is beyond the server's reach.</exception>
    /// <exception cref="ArgumentException">The section's name or the server's is blank.</exception>
    public static McpEndpoint Read(IConfiguration settings, Uri defaultUrl, McpEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Read(settings, defaultUrl, isHttpOnly: false, Environment.GetEnvironmentVariable(ContainerVariable), ServerOf(options), options);
    }

    /// <summary>The endpoint the settings of a server that answers only over HTTP ask for: HTTP whatever they name, and a transport or a port that would mean stdio refused.</summary>
    /// <remarks>
    /// The same keys and the same interfaces as <see cref="Read(IConfiguration, Uri)"/>. <c>Transport=Stdio</c> and
    /// <c>Port=0</c> are refused here, at the start, naming the variable: read as stdio, they came out an endpoint no HTTP
    /// builder could take, and the server stopped later with an <see cref="ArgumentException"/> that named neither
    /// (Snail.MCP.Sandbox, 0.5.0).
    /// </remarks>
    /// <exception cref="StartRefusedException">A key holds what it cannot mean for an HTTP server: a transport other than Http, a URL that is not an absolute http one, a port that is not one above zero, a flag that is not one.</exception>
    public static McpEndpoint ReadHttp(IConfiguration settings, Uri defaultUrl) =>
        Read(settings, defaultUrl, isHttpOnly: true, Environment.GetEnvironmentVariable(ContainerVariable), ServerSettings.NameOf(Assembly.GetEntryAssembly()));

    /// <summary>The endpoint of a server that answers only over HTTP whose settings may keep its keys in a section as well as at the root.</summary>
    /// <param name="settings">The server's settings, read at their root first.</param>
    /// <param name="defaultUrl">Where HTTP listens when the settings name no address.</param>
    /// <param name="fallbackSection">The section a key is looked for in when the root does not hold it, such as <c>Snail</c>.</param>
    /// <remarks>
    /// <see cref="ReadHttp(IConfiguration, Uri)"/>, each key looked for at the root and then in the section, as
    /// <see cref="Read(IConfiguration, Uri, string)"/> looks for it.
    /// </remarks>
    /// <exception cref="StartRefusedException">A key holds what it cannot mean for an HTTP server: a transport other than Http, a URL that is not an absolute http one, a port that is not one above zero, a flag that is not one.</exception>
    /// <exception cref="ArgumentException">The section's name is empty or blank.</exception>
    public static McpEndpoint ReadHttp(IConfiguration settings, Uri defaultUrl, string fallbackSection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackSection);

        return Read(settings, defaultUrl, isHttpOnly: true, Environment.GetEnvironmentVariable(ContainerVariable), ServerSettings.NameOf(Assembly.GetEntryAssembly()), fallbackSection);
    }

    /// <summary>The endpoint of a server that answers only over HTTP, read as <paramref name="options"/> say: the section also looked in, and how far beyond loopback the server may listen.</summary>
    /// <param name="settings">The server's settings, read at their root first.</param>
    /// <param name="defaultUrl">Where HTTP listens when the settings name no address.</param>
    /// <param name="options">The fallback section, as <see cref="ReadHttp(IConfiguration, Uri, string)"/> takes it, the server's <see cref="McpReach"/>, and the name its variables are spelled by.</param>
    /// <remarks><see cref="ReadHttp(IConfiguration, Uri)"/>, held to the reach as <see cref="Read(IConfiguration, Uri, McpEndpointOptions)"/> holds it.</remarks>
    /// <exception cref="StartRefusedException">A key holds what it cannot mean for an HTTP server, as for <see cref="ReadHttp(IConfiguration, Uri)"/>, or the endpoint is beyond the server's reach.</exception>
    /// <exception cref="ArgumentException">The section's name or the server's is blank.</exception>
    public static McpEndpoint ReadHttp(IConfiguration settings, Uri defaultUrl, McpEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Read(settings, defaultUrl, isHttpOnly: true, Environment.GetEnvironmentVariable(ContainerVariable), ServerOf(options), options);
    }

    /// <summary>The server's name its variables are spelled by: the one the options give, else the one the entry assembly carries.</summary>
    private static string? ServerOf(McpEndpointOptions options)
    {
        if (options.Server is null)
            return ServerSettings.NameOf(Assembly.GetEntryAssembly());

        ArgumentException.ThrowIfNullOrWhiteSpace(options.Server, nameof(options));

        return options.Server;
    }

    /// <summary>The endpoint the settings ask for, with the container variable, the server's name and the fallback section as given.</summary>
    internal static McpEndpoint Read(IConfiguration settings, Uri defaultUrl, bool isHttpOnly, string? containerVariable, string? server, string? fallbackSection = null) =>
        Read(settings, defaultUrl, isHttpOnly, containerVariable, server, new McpEndpointOptions { FallbackSection = fallbackSection });

    /// <summary>The endpoint the settings ask for, with the container variable, the server's name and the options as given.</summary>
    internal static McpEndpoint Read(IConfiguration settings, Uri defaultUrl, bool isHttpOnly, string? containerVariable, string? server, McpEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(defaultUrl);

        if (options.FallbackSection is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(options.FallbackSection, nameof(options));

        var keys = new SettingKeys(settings, options.FallbackSection, server);

        var transportSetting = keys.Of("Transport");
        var transport = TransportOf(transportSetting, isHttpOnly);
        var port = PortOf(keys.Of("Port"), isHttpOnly);
        var address = keys.Of("Url", "Http:Url");
        var url = UrlOf(address, defaultUrl);

        if (port > 0)
            url = new UriBuilder(url) { Port = port }.Uri;

        var chosen = transport ?? (port > 0 || isHttpOnly ? McpTransport.Http : McpTransport.Stdio);
        var flag = keys.Of("ListensOnEveryInterface");
        var reach = new Reach(options.Reach, url, address, IsTrue(flag) ? flag : null, IsInContainer(containerVariable) ? containerVariable : null);

        if (chosen is McpTransport.Http)
            reach.Check();
        else if (reach.Flag is { } everyInterface)
            throw new StartRefusedException($"{everyInterface.Name} is '{everyInterface.Value}'; the server answers over stdio, which opens no port, so it is false, or unset — or {transportSetting.Name} is Http.");

        return new McpEndpoint(chosen, url, reach.ListensOnEveryInterface) { UrlSetting = address.Value is null ? null : address.Path };
    }

    /// <summary>A key's name as a refusal says it: <c>SNAIL_MCP_DEMO_HTTP__URL</c> beside <c>Http:Url</c>, for a key at <c>Http:Url</c> of the demo server.</summary>
    internal static string VariableOf(string server, string path)
    {
        var sections = path.Split(':').Select(ScreamingSnake);

        return $"{ServerSettings.PrefixOf(server)}{string.Join("__", sections)}";
    }

    private static McpTransport? TransportOf(Setting setting, bool isHttpOnly)
    {
        var (value, name, _) = setting;

        if (string.IsNullOrWhiteSpace(value))
            return null;

        var isTransport = Enum.TryParse<McpTransport>(value.Trim(), ignoreCase: true, out var transport) && Enum.IsDefined(transport);

        if (isTransport && (!isHttpOnly || transport is McpTransport.Http))
            return transport;

        throw new StartRefusedException(isHttpOnly
            ? $"{name} is '{value}'; the server answers only over HTTP, so it is Http, or unset."
            : $"{name} is '{value}'; it is Stdio or Http.");
    }

    private static int PortOf(Setting setting, bool isHttpOnly)
    {
        var (value, name, _) = setting;

        if (string.IsNullOrWhiteSpace(value))
            return 0;

        var isPort = int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) && port is >= 0 and <= ushort.MaxValue;

        if (isPort && (!isHttpOnly || port > 0))
            return port;

        throw new StartRefusedException(isHttpOnly
            ? $"{name} is '{value}'; the server answers only over HTTP, so it is a port number above zero, or unset."
            : $"{name} is '{value}'; it is a port number, or 0 for stdio.");
    }

    private static Uri UrlOf(Setting setting, Uri defaultUrl)
    {
        var (value, name, _) = setting;

        if (string.IsNullOrWhiteSpace(value))
            return defaultUrl;

        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttp)
            return url;

        throw new StartRefusedException($"{name} is '{value}'; it is an absolute http address, such as {defaultUrl}.");
    }

    private static bool IsTrue(Setting setting)
    {
        var (value, name, _) = setting;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (bool.TryParse(value.Trim(), out var flag))
            return flag;

        throw new StartRefusedException($"{name} is '{value}'; it is true or false.");
    }

    private static bool IsInContainer(string? containerVariable) =>
        string.Equals(containerVariable?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>A key's value, its name as a refusal says it, and its path in the settings.</summary>
    private readonly record struct Setting(string? Value, string Name, string Path);

    /// <summary>Where an endpoint over HTTP would listen, held to the server's reach.</summary>
    /// <param name="Allowed">How far beyond loopback the server may listen.</param>
    /// <param name="Url">The address, its port replaced by a <c>Port</c>.</param>
    /// <param name="Address">The key the address was read from; unset when it is the server's default.</param>
    /// <param name="Flag"><c>ListensOnEveryInterface</c> when it is true; null otherwise.</param>
    /// <param name="Container">The container variable's value when it says the process runs in a container; null otherwise.</param>
    private sealed record Reach(McpReach Allowed, Uri Url, Setting Address, Setting? Flag, string? Container)
    {
        private const string Loopback = "this server answers on loopback alone, since its endpoint asks for no key";

        private const string LoopbackOutsideAContainer = "this server answers on loopback alone outside a container, and on every interface only inside one, since its endpoint asks for no key";

        public bool ListensOnEveryInterface => Flag is not null || Container is not null || IsUnspecified(Url);

        /// <summary>What the reach allows, in the words of a refusal: loopback alone, or loopback with every interface inside a container.</summary>
        /// <remarks>A refusal outside a container said "loopback alone" to a server that may open every interface in its image, and its operator was told nothing of the way that is allowed (Snail.MCP.Security, Git).</remarks>
        private string Allows => Allowed is McpReach.LoopbackOrContainer ? LoopbackOutsideAContainer : Loopback;

        /// <summary>Refuses an endpoint beyond the reach, or one on a host name it cannot listen on, naming what the operator set.</summary>
        public void Check()
        {
            var isNarrowed = Allowed is not McpReach.Unrestricted;
            var isContainerAllowed = Allowed is not McpReach.LoopbackOnly;

            if (Container is not null && !isContainerAllowed)
                throw new StartRefusedException($"{ContainerVariable} is '{Container}'; inside a container the port is opened on every interface, and {Loopback}: it runs outside a container.");

            if (Container is not null)
                return;

            if (isNarrowed && Flag is { } flag)
                throw new StartRefusedException($"{flag.Name} is '{flag.Value}'; {Allows}, so it is false, or unset.");

            if (isNarrowed && IsUnspecified(Url))
                throw new StartRefusedException($"{AddressSaid()}; it opens every interface, and {Allows}: it is 127.0.0.1, [::1] or localhost.");

            if (ListensOnEveryInterface)
                return;

            if (!IPAddress.TryParse(Url.DnsSafeHost, out var ip))
            {
                if (!string.Equals(Url.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase))
                    throw new StartRefusedException($"{AddressSaid()}; the server listens on an address, so its host is an IP address or localhost, or 0.0.0.0 for every interface.");

                return;
            }

            if (isNarrowed && !IPAddress.IsLoopback(ip))
                throw new StartRefusedException($"{AddressSaid()}; it is beyond loopback, and {Allows}: it is 127.0.0.1, [::1] or localhost.");
        }

        private string AddressSaid() =>
            Address.Value is null ? $"The server's default address is '{Url}'" : $"{Address.Name} is '{Address.Value.Trim()}'";
    }

    /// <summary>The keys of these settings: at the root first, then in the fallback section, each named in a refusal by the server's variable and the key of its settings file, or by the key alone.</summary>
    private sealed class SettingKeys(IConfiguration settings, string? fallbackSection, string? server)
    {
        /// <summary>The first of <paramref name="keys"/> the root holds, else the first the section holds, else the first key, unset.</summary>
        public Setting Of(params string[] keys)
        {
            IConfiguration[] places = fallbackSection is null ? [settings] : [settings, settings.GetSection(fallbackSection)];

            foreach (var place in places)
            {
                foreach (var key in keys)
                {
                    if (place[key] is { } value)
                        return new Setting(value, NameOf(PathOf(place, key)), PathOf(place, key));
                }
            }

            return new Setting(null, NameOf(PathOf(settings, keys[0])), PathOf(settings, keys[0]));
        }

        private static string PathOf(IConfiguration place, string key) =>
            place is IConfigurationSection section ? $"{section.Path}:{key}" : key;

        private string NameOf(string path) =>
            server is null ? path : ServerSettings.NameOfSetting(server, path);
    }
}

namespace Snail.Web.Template.Proxy;

/// <summary>The proxies in front of the application whose X-Forwarded headers it believes.</summary>
/// <remarks>
/// Nothing is believed by default: a header any client can write would otherwise decide the address the limit per
/// client counts and whether a request came over HTTPS. The proxy's network is named instead, such as the Docker
/// network traefik runs in.
/// </remarks>
internal sealed class ProxyOptions
{
    /// <summary>The configuration section the options are read from.</summary>
    public const string SectionName = "Proxy";

    /// <summary>The networks the proxy connects from, in CIDR form, such as 172.18.0.0/16.</summary>
    public IReadOnlyList<string> Networks { get; set; } = [];
}

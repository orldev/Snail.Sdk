using System.Net;

namespace Snail.Web.Template.Proxy;

/// <summary>The proxy's networks read from their CIDR form.</summary>
internal static class ProxyNetworks
{
    /// <summary>The networks written in CIDR form; anything else is left out, and <see cref="Malformed"/> names it.</summary>
    public static IReadOnlyList<IPNetwork> Parse(IEnumerable<string> networks) =>
        [.. networks.Where(IsNetwork).Select(network => IPNetwork.Parse(network))];

    /// <summary>What is written where a network belongs and is not one.</summary>
    public static IReadOnlyList<string> Malformed(IEnumerable<string> networks) =>
        [.. networks.Where(network => !IsNetwork(network))];

    private static bool IsNetwork(string network) => IPNetwork.TryParse(network, out _);
}

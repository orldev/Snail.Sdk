using System.Net;
using System.Net.Sockets;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>Ports a test hands to the servers and stand-ins it starts.</summary>
public static class Ports
{
    /// <summary>A loopback TCP port nothing listens on at this moment.</summary>
    /// <remarks>
    /// The system picks it — a listener bound to port 0 — and the listener is closed before the port is returned, so the
    /// server the test starts can bind it. Another process may take it in between; on a machine that is not starting
    /// servers by the hundred that does not happen, and a server that cannot bind exits and says so. Seventeen servers'
    /// tests kept a copy of these lines.
    /// </remarks>
    public static int Free()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

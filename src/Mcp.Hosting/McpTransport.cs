namespace Snail.Sdk.Mcp.Hosting;

/// <summary>How clients reach a server.</summary>
public enum McpTransport
{
    /// <summary>One process per client, started by it, over its stdin and stdout; the server lives as long as the client.</summary>
    Stdio,

    /// <summary>One process every client shares, over Streamable HTTP at an address.</summary>
    Http,
}

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>How far beyond loopback a server's HTTP endpoint may listen, which <see cref="McpEndpoint"/> holds its settings to when it reads them.</summary>
/// <remarks>
/// <para>
/// A server whose endpoint asks for no key narrows it: without a key, the address is the only thing between the server
/// and whoever reaches it. Before 0.5.0, <c>McpHttpHost.CreateBuilder(args, url)</c> refused an address beyond
/// loopback; <see cref="McpEndpoint"/> opens every interface for <c>ListensOnEveryInterface</c>, a URL at
/// <c>0.0.0.0</c> and a container alike, so a keyless server that did not narrow its reach could be opened to the
/// network by one setting (Snail.MCP.Memory, 0.5.0-local.3).
/// </para>
/// <para>
/// A refusal names what the operator set — <c>SNAIL_MCP_&lt;NAME&gt;_URL (or Url in the settings file) is '…'</c>, or
/// <c>DOTNET_RUNNING_IN_CONTAINER is 'true'</c> — at the start, before any host exists. Over stdio nothing is refused,
/// since nothing listens.
/// </para>
/// </remarks>
public enum McpReach
{
    /// <summary>Loopback unless the settings say otherwise: every interface for <c>ListensOnEveryInterface</c>, a URL at <c>0.0.0.0</c> or <c>[::]</c>, and inside a container; an IP address beyond loopback — a LAN one — as named.</summary>
    /// <remarks>For a server that guards its endpoint itself — Snail.MCP.Blender's and AfterEffects' tokens — and for one that answers on every interface by design, as Snail.MCP.E2E does.</remarks>
    Unrestricted,

    /// <summary>Loopback on the machine, and every interface inside a container, whose published port is the boundary; anything else beyond loopback refused.</summary>
    /// <remarks>
    /// For a keyless server that also ships an image — Snail.MCP.Git, FileSystem, Documents, Playwright, Roslyn, Sandbox,
    /// Security, Connector, and the server of <c>dotnet new s3l-mcp</c>: in the image the port is opened on every
    /// interface as before, and an image that names <c>0.0.0.0</c> keeps working; on the machine itself
    /// <c>ListensOnEveryInterface=true</c>, a URL at <c>0.0.0.0</c> and a URL at an address beyond loopback are refused.
    /// It is what <c>CreateBuilder(args, url)</c> and <c>CreateContainerBuilder</c> held before 0.5.0.
    /// </remarks>
    LoopbackOrContainer,

    /// <summary>Loopback alone, wherever the server runs: an address beyond it refused, and a start inside a container too.</summary>
    /// <remarks>For a keyless server that ships no image — Snail.MCP.Memory, CodeContext, Figma's MCP endpoint, and Plans or Obsidian once they serve HTTP.</remarks>
    LoopbackOnly,
}

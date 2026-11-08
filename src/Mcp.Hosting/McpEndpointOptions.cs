namespace Snail.Sdk.Mcp.Hosting;

/// <summary>How <see cref="McpEndpoint"/> reads a server's settings: the section it also looks in, how far beyond loopback the server may listen, and the name its refusals spell the variables by.</summary>
public sealed record McpEndpointOptions
{
    /// <summary>The section a key is looked for in when the root of the settings does not hold it, such as <c>Snail</c>; the root alone when null.</summary>
    public string? FallbackSection { get; init; }

    /// <summary>How far beyond loopback the server may listen; <see cref="McpReach.LoopbackOrContainer"/>, as the reads without options read, unless the server names another.</summary>
    public McpReach Reach { get; init; }

    /// <summary>The server's short name, as <see cref="ServerSettings"/> takes it — <c>figma</c> — which a refusal spells the variables by: <c>SNAIL_MCP_FIGMA_URL (or Url in the settings file)</c>; null takes it from the entry assembly.</summary>
    /// <remarks>
    /// The entry assembly names the server only in its own program: a test that reads the endpoint in its own process
    /// has the test runner for its entry assembly, and its refusals named the key alone, so a test of the variable a
    /// refusal names could not be written (Snail.MCP.FileSystem, Playwright, CodeContext). A server passes
    /// the name it reads its settings by, the same constant as <c>ServerSettings.Read(name)</c>.
    /// </remarks>
    public string? Server { get; init; }
}

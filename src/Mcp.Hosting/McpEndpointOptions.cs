namespace Snail.Sdk.Mcp.Hosting;

/// <summary>How <see cref="McpEndpoint"/> reads a server's settings: the section it also looks in, and how far beyond loopback the server may listen.</summary>
public sealed record McpEndpointOptions
{
    /// <summary>The section a key is looked for in when the root of the settings does not hold it, such as <c>Snail</c>; the root alone when null.</summary>
    public string? FallbackSection { get; init; }

    /// <summary>How far beyond loopback the server may listen; <see cref="McpReach.Unrestricted"/>, as the reads without options read, unless the server narrows it.</summary>
    public McpReach Reach { get; init; }
}

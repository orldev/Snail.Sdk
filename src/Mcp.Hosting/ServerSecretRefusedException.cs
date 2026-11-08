namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The refusal of a secret's file that holds something else than the secret: the file is left as it is.</summary>
/// <remarks>
/// A type of its own, so a server can add its own consequence to the refusal — name the plugin that has to be installed
/// again, the code that has to be handed out anew — without reading the message (Snail.MCP.Security, 0.5.0). It stays an
/// <see cref="InvalidOperationException"/>, which a server that catches that keeps catching.
/// </remarks>
/// <param name="file">The file, as the server named it.</param>
/// <param name="found">What the file holds, in words that never show its content: <c>12 bytes</c>.</param>
/// <param name="expected">What the secret is, in words: <c>32 bytes</c>, <c>64 hex digits</c>.</param>
public sealed class ServerSecretRefusedException(string file, string found, string expected)
    : InvalidOperationException($"{file} holds {found} instead of {expected}. It is left as it is: a new secret would invalidate every one handed out with it. Restore it, or remove it to have a new one made.")
{
    /// <summary>The file, as the server named it.</summary>
    public string File { get; } = file;

    /// <summary>What the file holds, in words that never show its content: <c>12 bytes</c>.</summary>
    public string Found { get; } = found;

    /// <summary>What the secret is, in words: <c>32 bytes</c>, <c>64 hex digits</c>.</summary>
    public string Expected { get; } = expected;
}

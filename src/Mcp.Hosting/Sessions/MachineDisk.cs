namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>The machine's own disk, asked through <see cref="File"/>, <see cref="Directory"/> and <see cref="FileInfo"/>.</summary>
internal sealed class MachineDisk : IRootsDisk
{
    /// <inheritdoc/>
    public bool IsFile(string path) => File.Exists(path);

    /// <inheritdoc/>
    public bool IsFolder(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public string? LinkTarget(string path) => new FileInfo(path).LinkTarget;
}

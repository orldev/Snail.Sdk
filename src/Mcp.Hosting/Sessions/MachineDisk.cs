namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>The machine's own disk, asked through <see cref="File"/> and <see cref="FileInfo"/>.</summary>
internal sealed class MachineDisk : IRootsDisk
{
    /// <inheritdoc/>
    public bool IsFile(string path) => Attributes(path) is { } attributes && !attributes.HasFlag(FileAttributes.Directory);

    /// <inheritdoc/>
    public bool IsFolder(string path) => Attributes(path) is { } attributes && attributes.HasFlag(FileAttributes.Directory);

    /// <inheritdoc/>
    public string? LinkTarget(string path) => new FileInfo(path).LinkTarget;

    /// <summary>What is at the path, or null when nothing is; a path the system may not look at throws rather than reads as missing.</summary>
    /// <remarks>
    /// <c>File.Exists</c> and <c>Directory.Exists</c> answer false for a file in a folder this process may not enter, so
    /// <c>Reach</c> told the model "there is no file" about a file that is there. A name under a file is still missing:
    /// nothing can be under a file, and the system says so as a folder not found.
    /// </remarks>
    /// <exception cref="UnauthorizedAccessException">The system may not look at the path.</exception>
    /// <exception cref="IOException">The disk failed while it was asked.</exception>
    private static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            return null;
        }
    }
}

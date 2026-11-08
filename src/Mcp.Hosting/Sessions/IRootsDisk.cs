namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>The disk <see cref="SessionRoots"/> reaches a path on: what is at a path, and where a link leads.</summary>
/// <remarks>
/// <para>
/// Everything <see cref="SessionRoots"/> and <see cref="RealPath"/> ask of a disk, and nothing more, so a server that puts
/// every disk access behind a port of its own hands them the same disk, and a test a disk that fails: a share that times
/// out, a file that vanishes between two calls. Snail.MCP.FileSystem's chaos tests failed its own port everywhere but at
/// <c>Reach</c>, which ran on the real disk whatever the test handed the server.
/// </para>
/// <para>
/// Any member may throw an <see cref="IOException"/> or an <see cref="UnauthorizedAccessException"/>: <c>Reach</c> answers
/// those as a refusal of the kind <see cref="Reached.Unreadable"/>, never as the server's failure.
/// </para>
/// </remarks>
public interface IRootsDisk
{
    /// <summary>The disk of the machine the server runs on, every question answered by the system: what <see cref="SessionRootsOptions.Disk"/> is unless a server sets another.</summary>
    static IRootsDisk Machine { get; } = new MachineDisk();

    /// <summary>Whether a file is at the path; false when nothing is.</summary>
    /// <param name="path">A full path with every link on it followed.</param>
    bool IsFile(string path);

    /// <summary>Whether a folder is at the path; false when nothing is.</summary>
    /// <param name="path">A full path with every link on it followed.</param>
    bool IsFolder(string path);

    /// <summary>Where the link at the path leads, as it is written — relative to the link's folder or absolute; null when the path is no link, or nothing is there.</summary>
    /// <param name="path">A full path whose folders are followed already; its last name is the one asked about.</param>
    string? LinkTarget(string path);
}

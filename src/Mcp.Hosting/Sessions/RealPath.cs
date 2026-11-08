namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>Where a path really leads, every link on the way followed.</summary>
/// <remarks>
/// <para>
/// Every link, not only the last one: <c>ResolveLinkTarget</c> answers about the final name alone, so a link in the middle —
/// <c>/copy/out/secret.txt</c> where <c>out</c> leads elsewhere — resolves to itself and walks out of the root. The fleet
/// wrote this four times (Snail.MCP.Documents, Git, FileSystem, Roslyn), and each copy took a path that does not exist as
/// the end of the walk: a link whose target is not there yet — <c>/copy/out</c> leading to <c>/etc/new</c> — passed as a
/// path inside the copy, and a write through it created the file outside.
/// </para>
/// <para>
/// So the walk goes name by name, as the system's own <c>realpath</c> does: a link is read even when what it leads to is
/// missing, a relative target is taken from the link's folder, and a <c>..</c> after a link climbs from where the link led,
/// not from the name it had. The names past the first missing one are kept as they are written — a file about to be
/// created has no link to follow, and the folders above it were.
/// </para>
/// </remarks>
public static class RealPath
{
    /// <summary>How many links a walk follows before it gives up: Linux's own limit, <c>MAXSYMLINKS</c>.</summary>
    public const int MostLinks = 40;

    /// <summary>The path with every link on it followed, or null when it cannot be: more than <paramref name="maxLinks"/> links, or one that may not be read.</summary>
    /// <param name="path">The path, absolute or relative to the working folder.</param>
    /// <param name="maxLinks">How many links to follow at most; a loop of links runs into it.</param>
    /// <param name="disk">The disk the links are read on: the server's own, the one its <see cref="SessionRootsOptions.Disk"/> names; the machine's when null.</param>
    public static string? Of(string path, int maxLinks = MostLinks, IRootsDisk? disk = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLinks);

        try
        {
            return Walk(path, maxLinks, disk ?? IRootsDisk.Machine);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The path with every link on it followed, or null when more than <paramref name="maxLinks"/> links lead on from it or no path can be written so.</summary>
    /// <remarks>What the disk throws goes on to the caller: <see cref="SessionRoots.Reach"/> tells a disk that failed from a loop of links.</remarks>
    /// <exception cref="IOException">The disk failed while a link was read.</exception>
    /// <exception cref="UnauthorizedAccessException">A link on the path may not be read.</exception>
    internal static string? Walk(string path, int maxLinks, IRootsDisk disk)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Join(Environment.CurrentDirectory, path);
        var real = Path.GetPathRoot(full)!;
        var ahead = new Stack<string>();
        var followed = 0;

        Push(ahead, full[real.Length..]);

        try
        {
            while (ahead.TryPop(out var name))
            {
                if (name is ".")
                    continue;

                if (name is "..")
                {
                    real = Path.GetDirectoryName(real) ?? real;
                    continue;
                }

                var next = Path.Join(real, name);

                if (LinkAt(disk, next) is not { } target)
                {
                    real = next;
                    continue;
                }

                if (++followed > maxLinks)
                    return null;

                if (Path.IsPathRooted(target))
                    real = Path.GetPathRoot(target)!;

                Push(ahead, Path.IsPathRooted(target) ? target[real.Length..] : target);
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        return real;
    }

    /// <summary>Puts the names of a path on what is still to walk, its first name on top.</summary>
    private static void Push(Stack<string> ahead, string path)
    {
        string[] names = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var at = names.Length - 1; at >= 0; at--)
            ahead.Push(names[at]);
    }

    /// <summary>Where the link at the path leads, as it is written; null when the path is no link or is not there.</summary>
    private static string? LinkAt(IRootsDisk disk, string path)
    {
        try
        {
            return disk.LinkTarget(path);
        }
        catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}

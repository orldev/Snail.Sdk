namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The name of the running program that outlives an update of it, for what it writes about itself.</summary>
/// <remarks>
/// A server that writes its own command line somewhere — hooks into a client's settings, a launch agent — has to name a
/// path that will still be there after the next publication. The path the system reports for a running program has
/// every link resolved; this undoes that for the two ways the servers are installed (docs/installing.md).
/// </remarks>
public static class Invocation
{
    /// <summary>
    /// A file of this program named the way that outlives an update: through <c>current</c> when it runs from the
    /// publication <c>current</c> points at, through the tool's command when it runs from a dotnet tool's store.
    /// </summary>
    /// <remarks>
    /// The path the system reports for a running program has every link resolved, and both ways of installing it keep a
    /// folder per version behind a link: a publication of <c>~/.s3l-mcp</c> behind <c>current</c> (docs/installing.md),
    /// a native tool package in <c>.store/&lt;id&gt;/&lt;version&gt;/</c> behind the command <c>dotnet tool</c> puts beside
    /// the store. A hook written with the version's folder breaks when an update removes it; written through the link, it
    /// follows every update.
    /// </remarks>
    public static string Stable(string path) => Command(path) ?? Current(path);

    /// <summary>The command of a dotnet tool that links to this file from beside the tool store; null when there is none.</summary>
    private static string? Command(string path)
    {
        var tools = ToolsFolder(path);

        if (tools is null)
            return null;

        try
        {
            return new DirectoryInfo(tools).EnumerateFiles()
                .FirstOrDefault(command => command.LinkTarget is { } target && Path.GetFullPath(target, tools) == path)?.FullName;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The folder that holds a dotnet tool's <c>.store</c> when the path lies inside that store, with either separator;
    /// null otherwise.
    /// </summary>
    internal static string? ToolsFolder(string path)
    {
        var store = path.IndexOf("/.store/", StringComparison.Ordinal);

        if (store < 0)
            store = path.IndexOf(@"\.store\", StringComparison.Ordinal);

        return store < 0 ? null : path[..store];
    }

    private static string Current(string path)
    {
        var folder = Path.GetDirectoryName(path);
        var home = Path.GetDirectoryName(folder);

        if (folder is null || home is null)
            return path;

        var current = Path.Combine(home, "current");

        try
        {
            return new DirectoryInfo(current).LinkTarget is { } target && Path.GetFileName(Path.TrimEndingDirectorySeparator(target)) == Path.GetFileName(folder)
                ? Path.Combine(current, Path.GetFileName(path))
                : path;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }
}

using System.ComponentModel;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The folders a server keeps in the user's home under its s3l name: its data and its settings.</summary>
/// <remarks>
/// Naming a folder never touches the disk, so a test that builds a server's settings in its own process, with the real
/// home, creates and moves nothing. For compatibility with the snail names, a folder is named by its snail name,
/// <c>~/.snail-mcp-&lt;name&gt;</c> or <c>~/.config/snail-mcp-&lt;name&gt;</c>, while that real folder is the only
/// one of the two.
/// </remarks>
public static class ServerFolders
{
    /// <summary>The user's home, resolved without touching the disk.</summary>
    internal static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary><c>~/.s3l-mcp-&lt;name&gt;</c>, where the server keeps its data; <paramref name="name"/> is its short name, such as figma.</summary>
    public static string DataDirectory(string name) => DataDirectory(name, Home);

    /// <summary><c>&lt;home&gt;/.s3l-mcp-&lt;name&gt;</c>, or <c>.snail-mcp-&lt;name&gt;</c> while only that one exists.</summary>
    internal static string DataDirectory(string name, string home)
    {
        var (current, former) = DataNames(name, home);

        return Named(current, former);
    }

    /// <summary><c>~/.config/s3l-mcp-&lt;name&gt;</c>, where the server's settings file lives.</summary>
    public static string ConfigDirectory(string name) => ConfigDirectory(name, Home);

    /// <summary><c>&lt;home&gt;/.config/s3l-mcp-&lt;name&gt;</c>, or <c>.config/snail-mcp-&lt;name&gt;</c> while only that one exists.</summary>
    internal static string ConfigDirectory(string name, string home)
    {
        var (current, former) = ConfigNames(name, home);

        return Named(current, former);
    }

    /// <summary>
    /// The compatibility move from the snail names: renames the server's folders from the snail name to the s3l name and
    /// leaves a link at the snail name, when the program is an installed one. It goes in the next major version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called as the server's program starts. Installed means published into <c>~/.s3l-mcp</c> or installed as a dotnet
    /// tool; a server run from a build's output — by its tests, or by <c>dotnet run</c> — moves nothing.
    /// </para>
    /// <para>
    /// A program that still reads the snail name finds the same files through the link. A refused move (the old folder
    /// is a container's mount point, or another process moved it a moment before) leaves the folders as they are. When
    /// the link cannot be made, the folder is moved back, so nothing is lost and the snail name keeps working. When both
    /// names are real folders and the snail one is not empty, one warning line on stderr names both, and the s3l one is
    /// the server's.
    /// </para>
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void MoveFromSnailNames(string name)
    {
        if (IsInstalled(AppContext.BaseDirectory, Home))
            MoveFromSnailNames(name, Home, Console.Error);
    }

    /// <summary>Moves <c>.snail-mcp-&lt;name&gt;</c> and <c>.config/snail-mcp-&lt;name&gt;</c> under the home to their s3l names, leaving links behind.</summary>
    internal static void MoveFromSnailNames(string name, string home, TextWriter warnings)
    {
        var (data, formerData) = DataNames(name, home);
        var (config, formerConfig) = ConfigNames(name, home);

        TryMove(formerData, data, LinkFolder, warnings);
        TryMove(formerConfig, config, LinkFolder, warnings);
    }

    /// <summary>
    /// Whether the old folder now lives under the new name, reached from the old one by <paramref name="link"/>; a refused
    /// move or link leaves the old folder where it was. Several sessions starting at once all try the move: the one that
    /// finds the new folder already there looks at the old name again before warning, since another process may have
    /// moved and linked it a moment before, which is nothing to warn about.
    /// </summary>
    internal static bool TryMove(string former, string current, Action<string, string> link, TextWriter warnings)
    {
        if (!Directory.Exists(former) || IsLink(former))
            return false;

        if (Directory.Exists(current))
        {
            var isStillTheOldFolder = Directory.Exists(former) && !IsLink(former);

            if (isStillTheOldFolder && !IsEmpty(former))
                warnings.WriteLine($"warn: both {former} and {current} exist; the server uses {current} and leaves {former} as it is");

            return false;
        }

        try
        {
            Directory.Move(former, current);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return Directory.Exists(current);
        }

        try
        {
            link(former, current);

            return true;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return !TryMoveBack(current, former, warnings);
        }
    }

    /// <summary>Whether the folder is back under its old name after its link could not be made.</summary>
    private static bool TryMoveBack(string current, string former, TextWriter warnings)
    {
        try
        {
            Directory.Move(current, former);

            return true;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            warnings.WriteLine($"warn: {former} moved to {current}, but neither a link at {former} nor the move back could be made");

            return false;
        }
    }

    private static void LinkFolder(string link, string target) => Directory.CreateSymbolicLink(link, target);

    private static (string Current, string Former) DataNames(string name, string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        return (Path.Combine(home, $".s3l-mcp-{name}"), Path.Combine(home, $".snail-mcp-{name}"));
    }

    private static (string Current, string Former) ConfigNames(string name, string home)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(home);

        var config = Path.Combine(home, ".config");

        return (Path.Combine(config, $"s3l-mcp-{name}"), Path.Combine(config, $"snail-mcp-{name}"));
    }

    private static string Named(string current, string former) =>
        !Directory.Exists(current) && Directory.Exists(former) && !IsLink(former) ? former : current;

    /// <summary>Whether a program in this folder was published into ~/.s3l-mcp or installed as a dotnet tool.</summary>
    internal static bool IsInstalled(string programFolder, string home)
    {
        var program = Path.GetFullPath(programFolder);
        var published = Path.GetFullPath(Path.Combine(home, ".s3l-mcp")) + Path.DirectorySeparatorChar;
        var tools = $"{Path.DirectorySeparatorChar}.store{Path.DirectorySeparatorChar}";

        return program.StartsWith(published, StringComparison.Ordinal) || program.Contains(tools, StringComparison.Ordinal);
    }

    /// <summary>Whether the folder holds nothing; one that cannot be read counts as holding something.</summary>
    private static bool IsEmpty(string folder)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(folder).Any();
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsLink(string folder) => new DirectoryInfo(folder).LinkTarget is not null;
}

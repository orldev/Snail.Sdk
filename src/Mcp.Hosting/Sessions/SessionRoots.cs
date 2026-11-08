using System.Diagnostics.CodeAnalysis;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Hosting.Sessions;

/// <summary>A path as a session may use it, or why it may not.</summary>
/// <remarks>
/// <see cref="IsAllowed"/> tells the compiler which of the two is there, and <see cref="As{T}"/> hands a refusal on as the
/// answer of a tool, its kind kept in <see cref="Type"/>: Snail.MCP.FileSystem wrote <c>Refused(reached)</c> for that, with
/// a <c>??</c> after the refusal the compiler could not see was always set.
/// </remarks>
/// <param name="Path">The path with every link on it followed, when the session may use it.</param>
/// <param name="Refusal">Why it may not, in words the caller can act on.</param>
/// <param name="Hint">What to do instead, when there is something.</param>
/// <param name="Type">The kind of refusal, a word a program can branch on — <see cref="Outside"/>, <see cref="Absent"/> and the rest; null when the path is allowed.</param>
public sealed record Reached(string? Path, string? Refusal, string? Hint, string? Type = null)
{
    /// <summary>The kind of a path outside every root of the session.</summary>
    public const string Outside = "outside";

    /// <summary>The kind of a session with no root to reach anything from.</summary>
    public const string NoRoot = "noRoot";

    /// <summary>The kind of a call that named no path.</summary>
    public const string NoPath = "noPath";

    /// <summary>The kind of a path whose links cannot be followed: a loop of them, or one that may not be read.</summary>
    public const string Unfollowable = "unfollowable";

    /// <summary>The kind of a file or a folder that is not there where one must be.</summary>
    public const string Absent = "absent";

    /// <summary>The kind of a folder where a file must be.</summary>
    public const string NotAFile = "notAFile";

    /// <summary>The kind of a file where a folder must be.</summary>
    public const string NotAFolder = "notAFolder";

    /// <summary>Why it may not, in words the caller can act on; never null when <see cref="Path"/> is.</summary>
    public string? Refusal { get; init; } = Path is null ? Refusal ?? Unexplained : Refusal;

    /// <summary>Whether the session may use the path; <see cref="Path"/> is then the one to use, and <see cref="Refusal"/> says why not otherwise.</summary>
    /// <exception cref="InvalidOperationException">The record was copied into one with neither a path nor a refusal.</exception>
    [MemberNotNullWhen(true, nameof(Path))]
    [MemberNotNullWhen(false, nameof(Refusal))]
    public bool IsAllowed
    {
        get
        {
            if (Path is not null)
                return true;

            return Refusal is not null ? false : throw new InvalidOperationException(Unexplained);
        }
    }

    /// <summary>The path, allowed.</summary>
    public static Reached Yes(string path) => new(path, null, null);

    /// <summary>A refusal, with what the caller can do about it and its kind.</summary>
    public static Reached No(string refusal, string? hint = null, string? type = null) => new(null, refusal, hint, type);

    /// <summary>The refusal as the failed answer of a tool, its words, hint and kind kept: <c>if (!reached.IsAllowed) return reached.As&lt;JsonObject&gt;();</c>.</summary>
    /// <exception cref="InvalidOperationException">The path is allowed, which has a path to use rather than a failure to hand on.</exception>
    public Answer<T> As<T>() =>
        IsAllowed ? throw new InvalidOperationException(AllowedHasNoRefusal) : Answer.Failed<T>(Refusal, Hint, Type);

    private const string Unexplained = "the path may not be used";

    private const string AllowedHasNoRefusal = "An allowed path carries the path to use, not a refusal: only a refusal goes on as a failed answer.";
}

/// <summary>What a path must be for <see cref="SessionRoots.Reach"/> to allow it.</summary>
public enum PathKind
{
    /// <summary>Anything, or nothing yet: a file about to be written.</summary>
    Any,

    /// <summary>A file that is there.</summary>
    File,

    /// <summary>A folder that is there.</summary>
    Folder,
}

/// <summary>The words <see cref="SessionRoots"/> refuses in where they are the server's own, and what a program configured with nothing reaches.</summary>
public sealed class SessionRootsOptions
{
    /// <summary>The setting that configures the server's roots — <c>SNAIL_MCP_DOCUMENTS_ROOTS</c> — named in the hint to a session that has none.</summary>
    /// <remarks>
    /// Null says "configure the server's roots". A hint that names the variable is one the operator can act on without
    /// reading the server's documentation (Snail.MCP.Documents named its own before it took these).
    /// </remarks>
    public string? RootsSetting { get; set; }

    /// <summary>Whether a server started as a program, with no volume configured, reaches every path its process can; false unless set.</summary>
    /// <remarks>
    /// <para>
    /// For a server a desktop client installs with no settings at all and starts over stdio for its one user — Snail.MCP.Roslyn
    /// opens whatever solution its caller names, and kept a <c>SessionReach</c> of its own to say so. Paths are still
    /// answered with every link followed, a relative one from the working folder, and <see cref="PathKind"/> still holds.
    /// </para>
    /// <para>
    /// Only where there is no address (<see cref="ISessionAddress.IsPresent"/>): a server over HTTP is reached by whoever
    /// reaches its port, and a session there that names no root at a server with no volume reaches nothing, as it did —
    /// <c>RefuseSessionsItCannotServe</c> turns it away at the door either way. A configured volume, or a root a session
    /// names, confines the server as before.
    /// </para>
    /// </remarks>
    public bool ReachesEverywhereAsAProgram { get; set; }
}

/// <summary>The folders this session works in, read from the address it was opened at, and whether a path lies in them.</summary>
/// <remarks>
/// <para>
/// A server that touches files is one call away from every file its process can read, so this is the whole of its
/// isolation. The roots come from the address — <c>…/mcp?root=/workspaces/task</c> — never from a call, which can say
/// whatever it likes. A session that names none works in the configured volumes, which is how a server started as a
/// program for one caller is confined; a server with neither reaches nothing rather than the disk.
/// </para>
/// <para>
/// Either the named roots or the volumes, never both. A shared server is configured with the whole volume of working
/// copies so it can serve any of them; adding it to what a session named would hand every session every other task's
/// files. And only named roots inside a volume: a session opened at <c>?root=/</c> was served the whole disk of the host
/// (Snail.MCP.FileSystem). Both sides are compared with their links followed (<see cref="RealPath"/>).
/// </para>
/// <para>
/// Read on every question, from the request being served. A job that outlives its request reads nothing there: it takes
/// the paths it needs while the request is still being served.
/// </para>
/// </remarks>
/// <param name="address">The address of the session.</param>
/// <param name="volumes">The folders the server was configured to serve; empty when it serves whatever a session names.</param>
/// <param name="options">The server's own words; the fleet's when null.</param>
public sealed class SessionRoots(ISessionAddress address, IReadOnlyList<string> volumes, SessionRootsOptions? options = null)
{
    /// <summary>What the address calls a root it hands over; repeated for more than one.</summary>
    public const string Parameter = "root";

    /// <summary>The roots this session works in: the ones it named inside the volumes, or the volumes when it named none.</summary>
    /// <remarks>Empty when every root it named is outside the volumes: such a session reaches nothing, and is refused at the door.</remarks>
    public IReadOnlyList<string> All
    {
        get
        {
            var named = Named;
            var configured = Paths(volumes);

            return named.Count == 0 ? configured : [.. named.Where(root => IsInside(root, configured))];
        }
    }

    /// <summary>The roots the address named, as full paths, each once.</summary>
    public IReadOnlyList<string> Named => Paths(address.Values(Parameter));

    /// <summary>Of the roots the address named, the ones outside every volume; none when no volume was configured.</summary>
    public IReadOnlyList<string> Outside
    {
        get
        {
            var configured = Paths(volumes);

            return configured.Count == 0 ? [] : [.. Named.Where(root => !IsInside(root, configured))];
        }
    }

    /// <summary>Of the roots the address named, the ones this machine has no folder for.</summary>
    /// <remarks>
    /// A root missing here means the copy is on another host: discovery handed the worker of one machine the server of
    /// another, and every answer would be about the wrong files — a wrong answer wearing the shape of a right one.
    /// </remarks>
    public IReadOnlyList<string> Absent => [.. Named.Where(root => !Directory.Exists(root))];

    /// <summary>Whether the address named a root outside every volume.</summary>
    public bool IsOutside => Outside.Count > 0;

    /// <summary>Whether the address named a root this machine does not have.</summary>
    public bool IsAbsent => Absent.Count > 0;

    /// <summary>Whether this session reaches every path its process can: a program with no volume, at a server that set <see cref="SessionRootsOptions.ReachesEverywhereAsAProgram"/>.</summary>
    /// <remarks><see cref="All"/> is empty then, since no root confines it, and a relative path is reached from the working folder.</remarks>
    public bool ReachesEverywhere =>
        options?.ReachesEverywhereAsAProgram == true && !address.IsPresent && Named.Count == 0 && Paths(volumes).Count == 0;

    /// <summary>What a refusal calls the one it speaks to: the address's <see cref="ISessionAddress.Subject"/>.</summary>
    internal string Subject => address.Subject;

    /// <summary>The path as the session may use it — absolute, or relative to the first root, every link followed — or why it may not.</summary>
    /// <remarks>
    /// <para>
    /// The resolved path rather than a yes or no: every caller needs it anyway, and resolving twice is how the check and
    /// the use drift apart — the check passing on one path and the write landing on another. A path that cannot be
    /// followed, a loop of links among them, is a refusal like any other.
    /// </para>
    /// <para>
    /// <paramref name="mustBe"/> holds a reading tool to a file and a listing one to a folder, in the fleet's words — "there
    /// is no file 'x'", "'x' is not a file" — which every server that reads wrote after this call by hand
    /// (Snail.MCP.Documents, FileSystem). <see cref="PathKind.Any"/>, the default, allows a path where nothing is yet.
    /// </para>
    /// <para>
    /// Every refusal carries its kind in <see cref="Reached.Type"/> — <see cref="Reached.Outside"/>,
    /// <see cref="Reached.Absent"/>, <see cref="Reached.NotAFile"/> and the rest — and <see cref="Reached.As{T}"/> hands it
    /// on as a tool's failed answer.
    /// </para>
    /// </remarks>
    /// <param name="path">The path the caller named.</param>
    /// <param name="mustBe">What must be there: a file, a folder, or anything or nothing.</param>
    public Reached Reach(string? path, PathKind mustBe = PathKind.Any)
    {
        var roots = All;
        var everywhere = roots.Count == 0 && ReachesEverywhere;

        if (roots.Count == 0 && !everywhere)
            return Unrooted();

        var from = everywhere ? Environment.CurrentDirectory : roots[0];

        if (string.IsNullOrWhiteSpace(path))
            return Reached.No("name a path", $"paths are absolute or relative to {from}", Reached.NoPath);

        var asked = path.Trim();
        var real = RealPath.Of(Path.IsPathRooted(asked) ? asked : Path.Join(from, asked));

        if (real is null)
        {
            return Reached.No($"'{asked}' cannot be followed: more than {RealPath.MostLinks} links lead on from it, or one of them may not be read",
                "a link on the path leads in a circle or somewhere this server may not look; name the path it leads to", Reached.Unfollowable);
        }

        if (!everywhere && !roots.Any(root => RealPath.Of(root) is { } inside && IsUnder(real, inside)))
            return Reached.No($"'{asked}' is outside this {Subject}, which works in {string.Join(", ", roots)}", type: Reached.Outside);

        return Kind(asked, real, mustBe);
    }

    /// <summary>The path as the caller should name it again: relative to the first root when it lies under it, whole otherwise.</summary>
    /// <remarks>
    /// The first root, because a relative path is reached from it. Against the root with its links followed, as the path
    /// was: a root behind a link — <c>/var</c> on macOS among them — made every answer name the host's real path.
    /// </remarks>
    public string Relative(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var roots = All;

        if (roots.Count == 0 || RealPath.Of(roots[0]) is not { } first)
            return path;

        return IsUnder(path, first) ? Path.GetRelativePath(first, path) : path;
    }

    /// <summary>The reached path when it is what the caller needs there, or what it is instead.</summary>
    private static Reached Kind(string asked, string real, PathKind mustBe)
    {
        bool isFile = File.Exists(real);
        bool isFolder = Directory.Exists(real);

        return mustBe switch
        {
            PathKind.File when isFolder => Reached.No($"'{asked}' is not a file", "that is a folder: name a file inside it", Reached.NotAFile),
            PathKind.File when !isFile => Reached.No($"there is no file '{asked}'", "check the name and the folder it is in", Reached.Absent),
            PathKind.Folder when isFile => Reached.No($"'{asked}' is not a folder", "that is a file: name the folder it is in", Reached.NotAFolder),
            PathKind.Folder when !isFolder => Reached.No($"there is no folder '{asked}'", "check the name and the folder it is in", Reached.Absent),
            _ => Reached.Yes(real),
        };
    }

    private Reached Unrooted()
    {
        var named = Named;

        if (named.Count > 0)
            return Reached.No($"this {Subject} named {string.Join(", ", named)}, which this server does not serve, so nothing is reachable", type: Reached.Outside);

        return Reached.No($"this {Subject} was given no root, so nothing is reachable",
            $"name the copy in the address, …?{Parameter}=/path/to/the/copy, or {Configure(options)}", Reached.NoRoot);
    }

    /// <summary>How the operator gives the server roots: the setting by its name when the server named it.</summary>
    internal static string Configure(SessionRootsOptions? options) =>
        options?.RootsSetting is { Length: > 0 } setting ? $"set {setting}" : "configure the server's roots";

    private static bool IsInside(string root, IReadOnlyList<string> configured) =>
        configured.Count == 0
        || (RealPath.Of(root) is { } real && configured.Any(volume => RealPath.Of(volume) is { } inside && IsUnder(real, inside)));

    /// <summary>Whether the path is the root or lies inside it; a sibling whose name only begins like the root's is not.</summary>
    /// <remarks>
    /// A root that ends in a separator already — <c>/</c>, <c>C:\</c>, which no trimming takes off — is the prefix as it is:
    /// with one more separator, a volume of <c>/</c> was <c>//</c> and held nothing (Snail.MCP.Roslyn).
    /// </remarks>
    internal static bool IsUnder(string path, string root)
    {
        var whole = Path.TrimEndingDirectorySeparator(root);
        var prefix = Path.EndsInDirectorySeparator(whole) ? whole : $"{whole}{Path.DirectorySeparatorChar}";

        return string.Equals(path, whole, StringComparison.Ordinal) || path.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>The roots as full paths, each once; a blank one, or one no path can be — a NUL in it — left out.</summary>
    private static IReadOnlyList<string> Paths(IEnumerable<string> roots) =>
    [
        .. roots
            .Where(root => !string.IsNullOrWhiteSpace(root) && !root.Contains('\0', StringComparison.Ordinal))
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Trim())))
            .Distinct(StringComparer.Ordinal)
    ];
}

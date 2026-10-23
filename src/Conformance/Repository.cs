using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Snail.Sdk.Conformance;

/// <summary>What a repository is to the standard: a library of Toolkit, an MCP server, or an ASP.NET application.</summary>
internal enum RepositoryKind
{
    Library,
    Server,
    Application
}

/// <summary>A repository as the checks read it: its folder, its kind and its files.</summary>
internal sealed class Repository
{
    private const string ServerPrefix = "Snail.MCP.";

    private const string TagPrefix = "refs/tags/";

    private static readonly EnumerationOptions Everywhere = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    private readonly IReadOnlyList<string> _templates;

    private Repository(string root, bool isServer, IReadOnlyList<string> templates)
    {
        Root = root;
        _templates = templates;
        Projects = [.. Files("*.csproj").Order(StringComparer.Ordinal)];
        Kind = isServer ? RepositoryKind.Server : Projects.Any(IsApplication) ? RepositoryKind.Application : RepositoryKind.Library;
    }

    /// <summary>The folder holding global.json and the solution.</summary>
    public string Root { get; }

    /// <summary>The repository's name, its folder's.</summary>
    public string Name => Path.GetFileName(Root);

    /// <summary>
    /// The server's short name, as every Snail.Sdk part derives it: the name without a leading Snail.MCP. in any case,
    /// without dots, in lower case — Snail.MCP.CleanUp gives cleanup.
    /// </summary>
    public string ShortName => ShortNameOf(Name);

    /// <summary>A library or a server.</summary>
    public RepositoryKind Kind { get; }

    /// <summary>Every project file outside bin and obj.</summary>
    public IReadOnlyList<string> Projects { get; }

    /// <summary>
    /// The repository at a folder, however the shell wrote the path: a server when it keeps .mcp/server.json or its name
    /// says so; otherwise an application when a project of its own, not a test, builds on the Web SDK; else a library.
    /// </summary>
    public static Repository At(string root)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var isServer = File.Exists(Path.Combine(full, ".mcp", "server.json")) || Path.GetFileName(full).StartsWith(ServerPrefix, StringComparison.OrdinalIgnoreCase);
        var templates = Directory.EnumerateDirectories(full, ".template.config", Everywhere).Select(folder => Path.GetDirectoryName(folder)!).ToList();

        return new Repository(full, isServer, templates);
    }

    /// <summary>Whether a project is an ASP.NET application: it names the Web SDK in its Sdk attribute and is none of the kinds.</summary>
    private static bool IsApplication(string project)
    {
        var sdks = (Project(project)?.Root?.Attribute("Sdk")?.Value ?? string.Empty).Split(';').Select(sdk => sdk.Split('/')[0].Trim());

        return sdks.Contains("Microsoft.NET.Sdk.Web", StringComparer.Ordinal) && KindOf(project).Length == 0;
    }

    /// <summary>The short name of a server's name: Snail.MCP. stripped in any case, dots dropped, lower case.</summary>
    public static string ShortNameOf(string name)
    {
        var rest = name.StartsWith(ServerPrefix, StringComparison.OrdinalIgnoreCase) ? name[ServerPrefix.Length..] : name;

        return rest.Replace(".", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    /// <summary>
    /// The tags of the repository's git history, read from its files: refs/tags and packed-refs of the git folder —
    /// found through the .git file of a worktree or a submodule as well as in a .git folder. Empty when there is no
    /// history, as in a checkout made without tags.
    /// </summary>
    public IReadOnlyList<string> Tags()
    {
        if (GitFolder() is not { } git)
            return [];

        var loose = Path.Combine(git, "refs", "tags");
        var tags = Directory.Exists(loose)
            ? Directory.EnumerateFiles(loose, "*", Everywhere).Select(file => Path.GetRelativePath(loose, file).Replace(Path.DirectorySeparatorChar, '/')).ToList()
            : [];
        var packed = Path.Combine(git, "packed-refs");

        if (File.Exists(packed))
        {
            var references = File.ReadLines(packed).Select(line => line.Split(' ')).Where(parts => parts.Length == 2).Select(parts => parts[1]);

            tags.AddRange(references.Where(reference => reference.StartsWith(TagPrefix, StringComparison.Ordinal)).Select(reference => reference[TagPrefix.Length..]));
        }

        return tags;
    }

    /// <summary>
    /// Whether a tag is the release being made rather than one made before: it names the commit checked out, or it is
    /// the tag the GitHub workflow runs for. A release checked out by its own tag holds that tag, and is no earlier release.
    /// </summary>
    public bool IsBeingReleased(string tag)
    {
        var isWorkflowTag = Environment.GetEnvironmentVariable("GITHUB_REF_TYPE") == "tag"
            && Environment.GetEnvironmentVariable("GITHUB_REF_NAME") == tag;

        if (isWorkflowTag)
            return true;

        var head = Head();

        return head is not null && head == Commit($"{TagPrefix}{tag}");
    }

    /// <summary>The commit checked out: HEAD of the worktree, followed through the branch it names. Null when unknown.</summary>
    private string? Head()
    {
        if (WorktreeGitFolder() is not { } worktree || !File.Exists(Path.Combine(worktree, "HEAD")))
            return null;

        var head = File.ReadAllText(Path.Combine(worktree, "HEAD")).Trim();

        return head.StartsWith("ref: ", StringComparison.Ordinal) ? Commit(head["ref: ".Length..].Trim()) : head;
    }

    /// <summary>
    /// The commit a reference names: its loose file, or its line in packed-refs — the peeled commit when an annotated
    /// tag's line is followed by one. Null when the reference is not there.
    /// </summary>
    private string? Commit(string reference)
    {
        if (GitFolder() is not { } git)
            return null;

        var loose = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(loose))
        {
            var named = File.ReadAllText(loose).Trim();

            return TaggedCommit(git, named) ?? named;
        }

        var packed = Path.Combine(git, "packed-refs");

        if (!File.Exists(packed))
            return null;

        var lines = File.ReadAllLines(packed);
        var at = Array.FindIndex(lines, line => line.EndsWith($" {reference}", StringComparison.Ordinal));

        if (at < 0)
            return null;

        var isPeeled = at + 1 < lines.Length && lines[at + 1].StartsWith('^');

        return isPeeled ? lines[at + 1][1..].Trim() : lines[at].Split(' ')[0];
    }

    /// <summary>
    /// The commit an annotated tag object names, when the object is loose — as `git tag -a` writes it — or null for any
    /// other object, or one already packed.
    /// </summary>
    private static string? TaggedCommit(string git, string objectId)
    {
        if (objectId.Length != 40)
            return null;

        var file = Path.Combine(git, "objects", objectId[..2], objectId[2..]);

        if (!File.Exists(file))
            return null;

        using var compressed = File.OpenRead(file);
        using var inflated = new ZLibStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(inflated, Encoding.ASCII);
        var header = reader.ReadLine() ?? string.Empty;

        if (!header.StartsWith("tag ", StringComparison.Ordinal))
            return null;

        var target = header[(header.IndexOf('\0', StringComparison.Ordinal) + 1)..];

        return target.StartsWith("object ", StringComparison.Ordinal) ? target["object ".Length..].Trim() : null;
    }

    /// <summary>The folder holding the repository's refs: .git itself, or what a .git file points to and its common folder.</summary>
    private string? GitFolder()
    {
        if (WorktreeGitFolder() is not { } folder)
            return null;

        var common = Path.Combine(folder, "commondir");

        return File.Exists(common) ? Path.GetFullPath(File.ReadAllText(common).Trim(), folder) : folder;
    }

    /// <summary>The git folder of this checkout: .git itself, or the folder a .git file points to — where its HEAD is.</summary>
    private string? WorktreeGitFolder()
    {
        var dotGit = Path.Combine(Root, ".git");

        if (Directory.Exists(dotGit))
            return dotGit;

        if (!File.Exists(dotGit) || File.ReadLines(dotGit).FirstOrDefault(line => line.StartsWith("gitdir:", StringComparison.Ordinal)) is not { } pointer)
            return null;

        return Path.GetFullPath(pointer["gitdir:".Length..].Trim(), Root);
    }

    /// <summary>A file of the repository by its path from the root, or null when there is none.</summary>
    public string? Text(string path)
    {
        var file = Path.Combine(Root, path);

        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    /// <summary>The files under the root whose names match, outside bin and obj and outside any dotnet new template it keeps.</summary>
    public IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Root, pattern, Everywhere).Where(file => IsSource(file) && !_templates.Any(template => file.StartsWith(template, StringComparison.Ordinal)));

    /// <summary>The repository's GitHub workflows: .github is hidden, so the walk of <see cref="Files"/> never reaches it.</summary>
    public IEnumerable<string> WorkflowFiles()
    {
        var folder = Path.Combine(Root, ".github", "workflows");

        return Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.yml").Order(StringComparer.Ordinal) : [];
    }

    /// <summary>A JSON file of the repository, or null when there is none or it does not parse.</summary>
    public JsonElement? Json(string path)
    {
        var text = Text(path);

        if (text is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A project file as XML, or null when it does not parse.</summary>
    public static XDocument? Project(string path)
    {
        try
        {
            return XDocument.Load(path);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// The Snail.Sdk level a project builds on, named in its Sdk attribute or — beside the Razor or Web SDK — as an
    /// Sdk element; otherwise the SDK of its attribute.
    /// </summary>
    public static string SdkOf(string project)
    {
        var root = Project(project)?.Root;
        var named = (root?.Attribute("Sdk")?.Value ?? string.Empty).Split(';').Select(sdk => sdk.Split('/')[0].Trim());
        var elements = root?.Elements("Sdk").Select(sdk => sdk.Attribute("Name")?.Value ?? string.Empty) ?? [];
        var all = named.Concat(elements).Where(sdk => sdk.Length > 0).ToList();

        return all.FirstOrDefault(sdk => sdk.StartsWith("Snail.Sdk", StringComparison.Ordinal)) ?? all.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// The kind of a project as Snail.Sdk decides it: Tests, Benchmarks, NativeProbe, Tool, or none. A SnailProjectKind the
    /// project file sets wins, as its body comes last; then the name; then one set in the nearest Directory.Build.props,
    /// which is read before the name and so only holds for a project whose name says nothing.
    /// </summary>
    public static string KindOf(string project)
    {
        var name = Path.GetFileNameWithoutExtension(project);
        var fromName = name.EndsWith(".Tests", StringComparison.Ordinal) ? "Tests"
            : name.EndsWith(".Benchmarks", StringComparison.Ordinal) ? "Benchmarks"
            : name.EndsWith(".NativeProbe", StringComparison.Ordinal) ? "NativeProbe"
            : string.Empty;

        return Property(Project(project), "SnailProjectKind") ?? (fromName.Length > 0 ? fromName : Inherited(project, "SnailProjectKind") ?? string.Empty);
    }

    /// <summary>Whether a project makes a package: none of the kinds does, nor a project that says IsPackable false.</summary>
    public static bool IsPacked(string project) =>
        KindOf(project).Length == 0 && !string.Equals(Property(Project(project), "IsPackable"), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>A property as the project sets it, or as the nearest Directory.Build.props above it does.</summary>
    public static string? Setting(string project, string name) => Property(Project(project), name) ?? Inherited(project, name);

    private static string? Property(XDocument? document, string name) =>
        document?.Root?.Elements("PropertyGroup").Elements(name).Select(element => element.Value.Trim()).LastOrDefault(value => value.Length > 0);

    /// <summary>A property the nearest Directory.Build.props above the project sets, up to the repository's global.json.</summary>
    private static string? Inherited(string project, string name)
    {
        for (var folder = Path.GetDirectoryName(Path.GetFullPath(project)); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            var props = Path.Combine(folder, "Directory.Build.props");

            if (File.Exists(props))
                return Property(Project(props), name);

            if (File.Exists(Path.Combine(folder, "global.json")))
                return null;
        }

        return null;
    }

    private static bool IsSource(string path) =>
        !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".git" or "node_modules" or "BenchmarkDotNet.Artifacts");
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Snail.Sdk.Docs;

/// <summary>A family as docs/tools.json names it: the class of tools, or the tools by name, the page it gets, the title it shows.</summary>
/// <remarks>
/// <see cref="Tools"/> names tools no class of the server declares — the ones Snail.Sdk registers, such as
/// <c>find_tool</c>, <c>run_tools</c>, <c>run_program</c> and the skill tools — in the order the page shows them. A family
/// names a class or tools, never both.
/// </remarks>
internal sealed record FamilyName(string? Type, string Slug, string Title, IReadOnlyList<string>? Tools = null);

/// <summary>
/// What a server says about its own pages, in docs/tools.json: the families in reading order, the words that open the
/// index, how a skill is loaded, and what the open-world hint means for it. Every part is optional; without the file
/// each class of tools is a family named after the class.
/// </summary>
internal sealed partial record CatalogSettings
{
    /// <summary>The slug of the page listing every tool, which no family may take.</summary>
    private const string Index = "index";

    /// <summary>The family of the tools no class declares and no family names: the ones Snail.Sdk registers.</summary>
    public static readonly FamilyName Registered = new(null, "discovery", "Finding and combining tools");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Paragraphs opening the index, after the count of tools.</summary>
    public IReadOnlyList<string> Intro { get; init; } = [];

    /// <summary>The line opening a skill's family page, with <c>{skill}</c> for its name.</summary>
    public string SkillNote { get; init; } = "Part of the `{skill}` skill: these tools appear once it is loaded.";

    /// <summary>The words for a tool whose open-world hint is set: it may reach beyond the machine.</summary>
    public string OpenWorld { get; init; } = "may reach outside services";

    /// <summary>The words for a tool whose open-world hint is not set.</summary>
    public string ClosedWorld { get; init; } = "stays on this machine";

    /// <summary>The families in reading order.</summary>
    public IReadOnlyList<FamilyName> Families { get; init; } = [];

    /// <summary>
    /// Reads the settings, or the defaults when there is no file. A file that is not JSON, holds a key the settings do
    /// not know, or names a family no page can be made of, stops the generator with what is wrong.
    /// </summary>
    public static CatalogSettings Read(string path)
    {
        if (!File.Exists(path))
            return new CatalogSettings();

        CatalogSettings? settings;

        try
        {
            settings = JsonSerializer.Deserialize<CatalogSettings>(File.ReadAllText(path), Options);
        }
        catch (JsonException invalid)
        {
            throw new DocsError(DocsError.InvalidSettings, $"{path}: {invalid.Message}");
        }

        var problems = Problems(settings ?? new CatalogSettings()).ToList();

        if (problems.Count > 0)
            throw new DocsError(DocsError.InvalidSettings, $"{path}: {string.Join("; ", problems)}");

        return settings ?? new CatalogSettings();
    }

    /// <summary>What keeps the settings from making pages: a missing part, a slug that is not a file name of its own, a title left empty.</summary>
    private static IEnumerable<string> Problems(CatalogSettings settings)
    {
        if (settings.Intro is null || settings.Intro.Any(paragraph => paragraph is null))
            yield return "intro must be a list of paragraphs";

        if (settings.SkillNote is null || settings.OpenWorld is null || settings.ClosedWorld is null)
            yield return "skillNote, openWorld and closedWorld must be text";

        if (settings.Families is null)
        {
            yield return "families must be a list";
            yield break;
        }

        foreach (var (family, index) in settings.Families.Select((family, index) => (family, index)))
        {
            foreach (var problem in FamilyProblems(family, $"families[{index}]"))
                yield return problem;
        }

        var named = settings.Families.OfType<FamilyName>().ToList();

        foreach (var slug in Repeated(named.Select(family => family.Slug)))
            yield return $"the slug {slug} is given to more than one family";

        foreach (var type in Repeated(named.Select(family => family.Type)))
            yield return $"the class {type} is named by more than one family";

        foreach (var tool in Repeated(named.SelectMany(family => family.Tools ?? [])))
            yield return $"the tool {tool} is named by more than one family";
    }

    private static IEnumerable<string> FamilyProblems(FamilyName? family, string where)
    {
        if (family is null)
        {
            yield return $"{where} is empty";
            yield break;
        }

        var namesTools = family.Tools is not null;

        if (string.IsNullOrWhiteSpace(family.Type) && !namesTools)
            yield return $"{where} names no class (type) and no tools";
        else if (family.Type is not null && namesTools)
            yield return $"{where} names a class (type) and tools: a family is one or the other";

        if (namesTools && (family.Tools!.Count == 0 || family.Tools.Any(string.IsNullOrWhiteSpace)))
            yield return $"{where}: tools must be a list of tool names";

        if (family.Slug is null || !SlugShape().IsMatch(family.Slug))
            yield return $"{where}: the slug \"{family.Slug}\" is not lower-case letters, digits and hyphens";
        else if (family.Slug == Index)
            yield return $"{where}: the slug {Index} is the page of every tool";

        if (string.IsNullOrWhiteSpace(family.Title))
            yield return $"{where} has no title";
    }

    private static IEnumerable<string> Repeated(IEnumerable<string?> values) =>
        values.OfType<string>().GroupBy(value => value, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key);

    /// <summary>Whether the settings name this class of tools.</summary>
    public bool Names(string type) => Families.Any(family => family.Type == type);

    /// <summary>The tools the settings name by name, in no family's order.</summary>
    public IReadOnlySet<string> NamedTools() => Families.SelectMany(family => family.Tools ?? []).ToHashSet(StringComparer.Ordinal);

    /// <summary>The family of a class: named by the settings, or after the class.</summary>
    public ToolFamily Family(string type, string? skill, IReadOnlyList<CatalogEntry> tools)
    {
        var named = Families.FirstOrDefault(family => family.Type == type);

        if (named is not null)
            return new ToolFamily(type, named.Slug, named.Title, skill, tools);

        var words = Words(type.EndsWith("Tools", StringComparison.Ordinal) ? type[..^"Tools".Length] : type);
        var slug = NotInSlug().Replace(string.Join('-', words).ToLowerInvariant(), "-");

        return new ToolFamily(type, slug == Index ? $"{slug}-tools" : slug, Sentence(words), skill, tools);
    }

    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var letter in name)
        {
            if (char.IsUpper(letter) && word.Length > 0 && !char.IsUpper(word[^1]))
            {
                words.Add(word.ToString());
                word.Clear();
            }

            word.Append(letter);
        }

        if (word.Length > 0)
            words.Add(word.ToString());

        return words;
    }

    private static string Sentence(List<string> words) =>
        string.Join(' ', words.Select((word, index) => index == 0 || word.All(char.IsUpper) ? word : word.ToLowerInvariant()));

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugShape();

    [GeneratedRegex("[^a-z0-9-]", RegexOptions.CultureInvariant)]
    private static partial Regex NotInSlug();
}

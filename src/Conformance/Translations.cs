using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Snail.Sdk.Conformance;

/// <summary>
/// The check of the repository's .resx files. A translation, <c>Name.&lt;culture&gt;.resx</c> beside an English
/// <c>Name.resx</c>, says everything the English file says and nothing else. Which languages there are is the
/// repository's choice: a language is added by adding its file.
/// </summary>
/// <remarks>
/// A key missing from a translation does not fail anything at run time: the resource manager falls back to English, and
/// the gap shows only to a reader of that language. A placeholder lost in translation does fail, with a FormatException
/// on the page that formats it. Both are visible in the files alone, so the check reads the files rather than waiting
/// for a reader to find them.
/// </remarks>
internal static partial class Translations
{
    /// <summary>How many keys a finding names before it counts the rest.</summary>
    private const int KeysShown = 3;

    /// <summary>The finding on the repository's resources.</summary>
    public static Finding Of(Repository repository)
    {
        var paths = repository.Files("*.resx").Select(file => Path.GetRelativePath(repository.Root, file).Replace(Path.DirectorySeparatorChar, '/')).ToHashSet(StringComparer.Ordinal);

        if (paths.Count == 0)
            return Rules.Translations.Met("no resources");

        var resources = paths.Order(StringComparer.Ordinal).Select(path => Resource.Of(path, paths)).ToList();
        var english = resources.Where(resource => resource.Language is null);
        var problems = english.SelectMany(neutral => Problems(repository, neutral, resources)).ToList();

        return problems.Count == 0
            ? Rules.Translations.Met()
            : Rules.Translations.Unmet(string.Join("; ", problems));
    }

    /// <summary>What one English file and its translations disagree on.</summary>
    private static IEnumerable<string> Problems(Repository repository, Resource neutral, List<Resource> resources)
    {
        if (Strings(repository, neutral.Path) is not { } texts)
        {
            yield return $"{neutral.Path} is not a resource file";
            yield break;
        }

        foreach (var problem in Empty(neutral.Path, texts))
            yield return problem;

        var translations = resources.Where(resource => resource.Language is not null && resource.English == neutral.Path).ToList();

        foreach (var translation in translations)
        {
            foreach (var problem in Disagreements(repository, neutral, texts, translation))
                yield return problem;
        }
    }

    /// <summary>How one translation departs from its English file: keys it lacks or adds, empty texts, other placeholders.</summary>
    private static IEnumerable<string> Disagreements(Repository repository, Resource neutral, Dictionary<string, string> english, Resource translation)
    {
        if (Strings(repository, translation.Path) is not { } translated)
        {
            yield return $"{translation.Path} is not a resource file";
            yield break;
        }

        var missing = english.Keys.Where(key => !translated.ContainsKey(key)).ToList();
        var extra = translated.Keys.Where(key => !english.ContainsKey(key)).ToList();
        var shared = english.Keys.Where(translated.ContainsKey);
        var drifted = shared.Where(key => !FormatItems(english[key]).SequenceEqual(FormatItems(translated[key]), StringComparer.Ordinal)).ToList();

        if (missing.Count > 0)
            yield return $"{translation.Path}: misses {Keys(missing)}";

        if (extra.Count > 0)
            yield return $"{translation.Path}: holds {Keys(extra)}, which {neutral.Path} does not";

        foreach (var problem in Empty(translation.Path, translated))
            yield return problem;

        if (drifted.Count > 0)
            yield return $"{translation.Path}: {Keys(drifted)} with other placeholders than in {neutral.Path}";
    }

    private static IEnumerable<string> Empty(string path, Dictionary<string, string> texts)
    {
        var empty = texts.Where(text => string.IsNullOrWhiteSpace(text.Value)).Select(text => text.Key).ToList();

        if (empty.Count > 0)
            yield return $"{path}: {Keys(empty)} empty";
    }

    /// <summary>
    /// The strings of a .resx by key — the data entries that carry no type, which are the ones a ResourceManager reads
    /// as text — or null when the file is not XML.
    /// </summary>
    private static Dictionary<string, string>? Strings(Repository repository, string path)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(repository.Text(path) ?? string.Empty);
        }
        catch (XmlException)
        {
            return null;
        }

        var entries = document.Root?.Elements("data").Where(data => data.Attribute("type") is null && data.Attribute("mimetype") is null) ?? [];
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (entry.Attribute("name")?.Value is { } name)
                strings.TryAdd(name, entry.Element("value")?.Value ?? string.Empty);
        }

        return strings;
    }

    private static string[] FormatItems(string text) =>
        [.. FormatItem().Matches(text).Select(item => item.Value).Order(StringComparer.Ordinal)];

    private static string Keys(List<string> keys)
    {
        var shown = string.Join(", ", keys.Take(KeysShown));

        return keys.Count > KeysShown ? $"{shown} and {keys.Count - KeysShown} more" : shown;
    }

    [GeneratedRegex(@"\{\d+(?:[,:][^}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex FormatItem();

    [GeneratedRegex(@"^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageName();

    /// <summary>
    /// One .resx by its path from the root: English when no English file of the same name stands beside it, otherwise the
    /// translation into the language its name ends in.
    /// </summary>
    private sealed record Resource(string Path, string English, string? Language)
    {
        public static Resource Of(string path, HashSet<string> paths)
        {
            var stem = path[..^".resx".Length];
            var dot = stem.LastIndexOf('.');
            var slash = stem.LastIndexOf('/');

            if (dot <= slash)
                return new(path, path, null);

            var language = stem[(dot + 1)..];
            var english = $"{stem[..dot]}.resx";

            return LanguageName().IsMatch(language) && paths.Contains(english)
                ? new(path, english, language)
                : new(path, path, null);
        }
    }
}

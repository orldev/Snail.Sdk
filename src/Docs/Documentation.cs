using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snail.Sdk.Docs;

/// <summary>The XML documentation file the compiler wrote, read as one line of Markdown per id.</summary>
internal sealed partial class Documentation
{
    private const int DeepestInheritance = 8;

    private readonly Dictionary<string, XElement> _members;
    private readonly Queue<string> _unread;

    private Documentation(Dictionary<string, XElement> members, IEnumerable<string> unread)
    {
        _members = members;
        _unread = new Queue<string>(unread);
    }

    /// <summary>
    /// Reads the file; a missing one gives a documentation with no summaries rather than a failure. The documentation
    /// beside each reference is read only when an inherited summary is looked for and the file at hand lacks it.
    /// </summary>
    public static Documentation Read(string path, IEnumerable<string>? references = null)
    {
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var beside = (references ?? []).Select(reference => Path.ChangeExtension(reference, ".xml"));

        Add(members, path);

        return new Documentation(members, beside);
    }

    /// <summary>The summary of an id as one line of Markdown, empty when it has none.</summary>
    public string Summary(string id) => Summary(id, [], DeepestInheritance);

    /// <summary>
    /// The summary of an id, following an inheritdoc: to its cref when it names one, otherwise to the members it could
    /// inherit from — the one it overrides, the interface members it implements.
    /// </summary>
    public string Summary(string id, IReadOnlyList<string> inheritedFrom) => Summary(id, inheritedFrom, DeepestInheritance);

    private string Summary(string id, IReadOnlyList<string> inheritedFrom, int depth)
    {
        if (depth == 0 || Find(id) is not { } member)
            return string.Empty;

        if (member.Element("summary") is { } summary)
            return Line(summary);

        if (member.Element("inheritdoc") is not { } inherited)
            return string.Empty;

        if (inherited.Attribute("cref")?.Value is { } cref)
            return Summary(cref, [], depth - 1);

        var found = inheritedFrom.Select(candidate => Summary(candidate, [], depth - 1)).FirstOrDefault(text => text.Length > 0);

        return found ?? string.Empty;
    }

    private XElement? Find(string id)
    {
        while (true)
        {
            if (_members.TryGetValue(id, out var member))
                return member;

            if (!_unread.TryDequeue(out var next))
                return null;

            Add(_members, next);
        }
    }

    private static void Add(Dictionary<string, XElement> members, string path)
    {
        if (!File.Exists(path))
            return;

        var declared = XDocument.Load(path).Descendants("member").Where(member => member.Attribute("name") is not null);

        foreach (var member in declared)
            members.TryAdd(member.Attribute("name")!.Value, member);
    }

    private static string Line(XElement element)
    {
        var text = new StringBuilder();

        foreach (var node in element.Nodes())
            text.Append(Inline(node));

        return Whitespace().Replace(text.ToString(), " ").Trim().Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string Inline(XNode node) => node switch
    {
        XText text => text.Value,
        XElement { Name.LocalName: "see" or "seealso" } reference => $"`{Referenced(reference)}`",
        XElement { Name.LocalName: "paramref" or "typeparamref" } parameter => $"`{parameter.Attribute("name")?.Value}`",
        XElement { Name.LocalName: "c" } code => $"`{code.Value}`",
        XElement { Name.LocalName: "para" } paragraph => $" {Line(paragraph)} ",
        XElement other => Line(other),
        _ => string.Empty
    };

    private static string Referenced(XElement reference)
    {
        if (reference.Attribute("langword")?.Value is { } keyword)
            return keyword;

        if (reference.Attribute("href")?.Value is { } link)
            return reference.Value.Length > 0 ? reference.Value : link;

        var id = reference.Attribute("cref")?.Value ?? string.Empty;
        var name = id.Contains(':', StringComparison.Ordinal) ? id[(id.IndexOf(':', StringComparison.Ordinal) + 1)..] : id;
        var withoutParameters = name.Contains('(', StringComparison.Ordinal) ? name[..name.IndexOf('(', StringComparison.Ordinal)] : name;
        var shortName = withoutParameters[(withoutParameters.LastIndexOf('.') + 1)..];

        return shortName.Replace("`", string.Empty, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

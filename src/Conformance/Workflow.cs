namespace Snail.Sdk.Conformance;

/// <summary>A line of YAML that holds something: its indentation (a list item's dash counted as indentation), and the key and value it writes.</summary>
internal sealed record YamlLine(int Number, int Indent, bool IsItem, string Key, string Value);

/// <summary>One call of a workflow or action of Snail.Sdk: the line it is written on, the job it is in, what it names and the inputs it passes, with their values.</summary>
internal sealed record SharedCall(int Line, string Job, string Target, IReadOnlyDictionary<string, string> With);

/// <summary>An input a workflow or action declares, and whether a caller must pass it.</summary>
internal sealed record WorkflowInput(string Key, bool IsRequired);

/// <summary>
/// A GitHub workflow or action read as lines, by their indentation — enough to find its jobs, the shared workflows and
/// actions it calls with the inputs it passes, and the inputs it declares, without a YAML parser.
/// </summary>
internal sealed class Workflow
{
    /// <summary>How a workflow names a workflow or action of Snail.Sdk.</summary>
    public const string Shared = "orldev/Snail.Sdk/";

    private readonly List<YamlLine> _lines;

    private Workflow(List<YamlLine> lines) => _lines = lines;

    /// <summary>Reads a workflow from its text.</summary>
    public static Workflow Parse(string text)
    {
        var lines = new List<YamlLine>();
        var number = 0;

        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            number++;
            var content = raw.TrimStart(' ');

            if (content.Length == 0 || content.StartsWith('#'))
                continue;

            var indent = raw.Length - content.Length;
            var isItem = content.StartsWith("- ", StringComparison.Ordinal);

            if (isItem)
            {
                indent += 2;
                content = content[2..];
            }

            var colon = content.IndexOf(':', StringComparison.Ordinal);
            var isKey = colon > 0 && (colon == content.Length - 1 || content[colon + 1] == ' ') && !content.StartsWith('"') && !content.StartsWith('\'');
            var key = isKey ? content[..colon] : string.Empty;
            var value = isKey ? content[(colon + 1)..].Trim() : content.Trim();

            lines.Add(new YamlLine(number, indent, isItem, key, value));
        }

        return new Workflow(lines);
    }

    /// <summary>Every call of a Snail.Sdk workflow (a job's uses) or action (a step's uses).</summary>
    public IReadOnlyList<SharedCall> Calls()
    {
        var calls = new List<SharedCall>();

        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];

            if (line.Key != "uses" || !Unquoted(line.Value).StartsWith(Shared, StringComparison.Ordinal))
                continue;

            var target = Unquoted(line.Value)[Shared.Length..].Split('@')[0];
            var with = Sibling(index, "with") is { } at
                ? Children(at).DistinctBy(child => child.Key).ToDictionary(child => child.Key, child => Unquoted(child.Value), StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

            calls.Add(new SharedCall(line.Number, JobOf(index), target, with));
        }

        return calls;
    }

    /// <summary>The inputs a shared workflow (under on.workflow_call) or action (at the top) declares.</summary>
    public IReadOnlyList<WorkflowInput> Inputs(bool isAction)
    {
        string[] path = isAction ? ["inputs"] : ["on", "workflow_call", "inputs"];
        var at = -1;

        foreach (var key in path)
        {
            at = ChildIndex(at, key);

            if (at < 0)
                return [];
        }

        return [.. Children(at).Select(child => new WorkflowInput(child.Key, IsRequired(child)))];
    }

    /// <summary>Whether a job may ask for an OIDC token: id-token write in its own permissions, or — when it sets none — in the workflow's.</summary>
    public bool GrantsIdToken(string job)
    {
        var jobs = ChildIndex(-1, "jobs");
        var at = jobs < 0 ? -1 : ChildIndex(jobs, job);
        var own = at < 0 ? -1 : ChildIndex(at, "permissions");
        var permissions = own >= 0 ? own : ChildIndex(-1, "permissions");

        if (permissions < 0)
            return false;

        return _lines[permissions].Value == "write-all" || Children(permissions).Any(line => line.Key == "id-token" && line.Value == "write");
    }

    /// <summary>The tag patterns a push starts the workflow on, written inline (['v*']) or as a list.</summary>
    public IReadOnlyList<string> TagPatterns()
    {
        var on = ChildIndex(-1, "on");
        var push = on < 0 ? -1 : ChildIndex(on, "push");
        var tags = push < 0 ? -1 : ChildIndex(push, "tags");

        if (tags < 0)
            return [];

        var inline = _lines[tags].Value;

        if (inline.Length > 0)
            return [.. inline.Trim('[', ']').Split(',').Select(Unquoted).Where(pattern => pattern.Length > 0)];

        return [.. Children(tags).Where(line => line.IsItem).Select(line => Unquoted(line.Value))];
    }

    private static string Unquoted(string value) => value.Trim().Trim('"', '\'');

    private bool IsRequired(YamlLine declaration) =>
        Children(_lines.IndexOf(declaration)).Any(child => child.Key == "required" && child.Value == "true");

    /// <summary>The lines one level under a line, or under the document when the index is -1.</summary>
    private List<YamlLine> Children(int parent)
    {
        var depth = parent < 0 ? -1 : _lines[parent].Indent;
        var children = new List<YamlLine>();
        var childIndent = -1;

        for (var index = parent + 1; index < _lines.Count && _lines[index].Indent > depth; index++)
        {
            if (childIndent < 0)
                childIndent = _lines[index].Indent;

            if (_lines[index].Indent == childIndent)
                children.Add(_lines[index]);
        }

        return children;
    }

    private int ChildIndex(int parent, string key)
    {
        var child = Children(parent).FirstOrDefault(line => line.Key == key);

        return child is null ? -1 : _lines.IndexOf(child);
    }

    /// <summary>The line of a key written beside the one at an index, in the same mapping (the same job or the same step).</summary>
    private int? Sibling(int at, string key)
    {
        var indent = _lines[at].Indent;

        for (var index = at + 1; index < _lines.Count && _lines[index].Indent >= indent; index++)
        {
            if (_lines[index].Indent != indent)
                continue;

            if (_lines[index].IsItem)
                return null;

            if (_lines[index].Key == key)
                return index;
        }

        return null;
    }

    /// <summary>The job a line belongs to: the key under jobs above it.</summary>
    private string JobOf(int at)
    {
        var jobs = ChildIndex(-1, "jobs");

        if (jobs < 0 || at < jobs)
            return string.Empty;

        var job = Children(jobs).LastOrDefault(line => _lines.IndexOf(line) < at);

        return job?.Key ?? string.Empty;
    }
}

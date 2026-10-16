using System.Text.RegularExpressions;

namespace Snail.Sdk.Sdk.Tests;

/// <summary>The templates' workflows call only what this repository keeps, the way it declares it.</summary>
/// <remarks>
/// A project made from a template calls the shared workflows and the push action by the major tag, so a call that names
/// a renamed file, passes a removed input or reads an output that is gone fails only on GitHub, in every project made
/// since. The files are read here as lines, by their indentation: every workflow and action a template names exists, takes
/// every input passed and is given every input it requires, and declares every output a template reads of it.
/// </remarks>
public sealed partial class TemplateWorkflowTests
{
    private const string Shared = "orldev/Snail.Sdk/";

    private static readonly string Root = RepositoryRoot();

    private static readonly IReadOnlyList<Call> Calls = TemplateCalls();

    [Fact]
    public void TemplateWorkflows_CallTheSharedWorkflowsAndThePushAction()
    {
        var targets = Calls.Select(call => call.Target).ToHashSet(StringComparer.Ordinal);

        Assert.Contains(".github/workflows/lib-ci.yml", targets);
        Assert.Contains(".github/workflows/lib-publish.yml", targets);
        Assert.Contains(".github/workflows/mcp-ci.yml", targets);
        Assert.Contains(".github/workflows/mcp-publish.yml", targets);
        Assert.Contains(".github/workflows/mcp-image.yml", targets);
        Assert.Contains(".github/actions/nuget-push", targets);
    }

    [Fact]
    public void SharedCall_InATemplateWorkflow_NamesAWorkflowOrActionOfThisRepository()
    {
        var missing = Calls.Where(call => !File.Exists(CalledFile(call.Target))).Select(call => $"{call.Where} calls {call.Target}, which is not here").ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void SharedCall_InATemplateWorkflow_PassesOnlyInputsTheCalledFileDeclares()
    {
        var undeclared = new List<string>();

        foreach (var call in Calls.Where(call => File.Exists(CalledFile(call.Target))))
        {
            var inputs = Declared(call.Target, "inputs").Select(input => input.Key).ToHashSet(StringComparer.Ordinal);

            undeclared.AddRange(call.With.Where(key => !inputs.Contains(key)).Select(key => $"{call.Where} passes {key}, not an input of {call.Target}"));
        }

        Assert.True(undeclared.Count == 0, string.Join(Environment.NewLine, undeclared));
    }

    [Fact]
    public void SharedCall_InATemplateWorkflow_PassesEveryRequiredInput()
    {
        var unpassed = new List<string>();

        foreach (var call in Calls.Where(call => File.Exists(CalledFile(call.Target))))
        {
            var required = Declared(call.Target, "inputs").Where(input => input.IsRequired).Select(input => input.Key);

            unpassed.AddRange(required.Where(key => !call.With.Contains(key, StringComparer.Ordinal)).Select(key => $"{call.Where} leaves out {key}, which {call.Target} requires"));
        }

        Assert.True(unpassed.Count == 0, string.Join(Environment.NewLine, unpassed));
    }

    [Fact]
    public void Output_ReadFromASharedWorkflow_IsOneItDeclares()
    {
        var undeclared = new List<string>();

        foreach (var workflow in TemplateWorkflowFiles())
        {
            var text = File.ReadAllText(workflow);
            var jobs = Calls.Where(call => call.File == workflow && call.Job is not null).ToDictionary(call => call.Job!, call => call.Target, StringComparer.Ordinal);

            foreach (Match read in NeedsOutput().Matches(text))
            {
                if (!jobs.TryGetValue(read.Groups["job"].Value, out var target) || !File.Exists(CalledFile(target)))
                    continue;

                var output = read.Groups["output"].Value;
                var outputs = Declared(target, "outputs").Select(declared => declared.Key);

                if (!outputs.Contains(output, StringComparer.Ordinal))
                    undeclared.Add($"{Relative(workflow)} reads {read.Value}, not an output of {target}");
            }
        }

        Assert.True(undeclared.Count == 0, string.Join(Environment.NewLine, undeclared));
    }

    /// <summary>One call of a workflow or action of this repository: where it is written, the job it is, what it names and the inputs it passes.</summary>
    private sealed record Call(string File, int Line, string? Job, string Target, IReadOnlyList<string> With)
    {
        public string Where => $"{Relative(File)}:{Line}";
    }

    /// <summary>An input or output a workflow or action declares.</summary>
    private sealed record Declaration(string Key, bool IsRequired);

    /// <summary>A line of YAML that holds something: its indentation (a list item's dash counted as indentation), and the key and value it writes.</summary>
    private sealed record Line(int Number, int Indent, bool IsItem, string Key, string Value);

    private static List<Call> TemplateCalls()
    {
        var calls = new List<Call>();

        foreach (var workflow in TemplateWorkflowFiles())
        {
            var lines = Read(workflow);

            for (var index = 0; index < lines.Count; index++)
            {
                var line = lines[index];

                if (line.Key != "uses" || !line.Value.StartsWith(Shared, StringComparison.Ordinal))
                    continue;

                var target = line.Value[Shared.Length..].Split('@')[0];
                var job = line.IsItem ? null : JobOf(lines, index);
                var with = Sibling(lines, index, "with") is { } at ? Children(lines, at).Select(child => child.Key).ToList() : [];

                calls.Add(new Call(workflow, line.Number, job, target, with));
            }
        }

        return calls;
    }

    private static IEnumerable<Declaration> Declared(string target, string section)
    {
        var lines = Read(CalledFile(target));
        string[] path = target.StartsWith(".github/workflows/", StringComparison.Ordinal) ? ["on", "workflow_call", section] : [section];
        var at = -1;

        foreach (var key in path)
        {
            at = ChildIndex(lines, at, key);

            if (at < 0)
                return [];
        }

        return [.. Children(lines, at).Select(child => new Declaration(child.Key, IsRequired(lines, child)))];
    }

    private static bool IsRequired(List<Line> lines, Line declaration)
    {
        var at = lines.IndexOf(declaration);

        return Children(lines, at).Any(child => child.Key == "required" && child.Value == "true");
    }

    private static List<Line> Read(string path)
    {
        var lines = new List<Line>();
        var number = 0;

        foreach (var text in File.ReadLines(path))
        {
            number++;
            var content = text.TrimStart(' ');

            if (content.Length == 0 || content.StartsWith('#'))
                continue;

            var indent = text.Length - content.Length;
            var isItem = content.StartsWith("- ", StringComparison.Ordinal);

            if (isItem)
            {
                indent += 2;
                content = content[2..];
            }

            var colon = content.IndexOf(':', StringComparison.Ordinal);
            var key = colon > 0 ? content[..colon] : string.Empty;
            var value = colon > 0 ? content[(colon + 1)..].Trim() : content;

            lines.Add(new Line(number, indent, isItem, key, value));
        }

        return lines;
    }

    /// <summary>The lines one level under a line, or under the document when the index is -1.</summary>
    private static List<Line> Children(List<Line> lines, int parent)
    {
        var depth = parent < 0 ? -1 : lines[parent].Indent;
        var children = new List<Line>();
        var childIndent = -1;

        for (var index = parent + 1; index < lines.Count && lines[index].Indent > depth; index++)
        {
            if (childIndent < 0)
                childIndent = lines[index].Indent;

            if (lines[index].Indent == childIndent)
                children.Add(lines[index]);
        }

        return children;
    }

    private static int ChildIndex(List<Line> lines, int parent, string key)
    {
        var child = Children(lines, parent).FirstOrDefault(line => line.Key == key);

        return child is null ? -1 : lines.IndexOf(child);
    }

    /// <summary>The line of a key written beside the one at an index, in the same mapping (the same job or the same step).</summary>
    private static int? Sibling(List<Line> lines, int at, string key)
    {
        var indent = lines[at].Indent;

        for (var index = at + 1; index < lines.Count && lines[index].Indent >= indent; index++)
        {
            if (lines[index].Indent != indent)
                continue;

            if (lines[index].IsItem)
                return null;

            if (lines[index].Key == key)
                return index;
        }

        return null;
    }

    /// <summary>The job a job-level uses belongs to: the nearest key written less indented above it.</summary>
    private static string JobOf(List<Line> lines, int at)
    {
        var indent = lines[at].Indent;
        var index = at - 1;

        while (lines[index].Indent >= indent)
            index--;

        return lines[index].Key;
    }

    private static IEnumerable<string> TemplateWorkflowFiles() =>
        Directory.EnumerateDirectories(Path.Combine(Root, "src", "Templates", "content"))
            .Select(template => Path.Combine(template, ".github", "workflows"))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.yml"))
            .Order(StringComparer.Ordinal);

    /// <summary>The file of a target: a workflow is a file, an action a folder holding action.yml.</summary>
    private static string CalledFile(string target) =>
        target.EndsWith(".yml", StringComparison.Ordinal) ? Path.Combine(Root, target) : Path.Combine(Root, target, "action.yml");

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Snail.Sdk.slnx")))
            folder = folder.Parent;

        return folder?.FullName ?? throw new InvalidOperationException("Snail.Sdk.slnx was not found above the tests.");
    }

    [GeneratedRegex(@"needs\.(?<job>[A-Za-z0-9_-]+)\.outputs\.(?<output>[A-Za-z0-9_-]+)")]
    private static partial Regex NeedsOutput();
}

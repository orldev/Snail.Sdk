using Snail.Sdk.Conformance;

const string usage = """
    Snail.Sdk.Conformance <repository> [--release]                              one repository; exit 1 when a required check fails
    Snail.Sdk.Conformance --fleet <folder>... [--report <file.md>] [--release]  every repository of the folders, as a table
    Snail.Sdk.Conformance --rules                                               every check: id, name, whom it asks, what holds
    --release adds the release feed check: nothing of the local feed is left.
    """;

var standard = Standard.Embedded();
var isRelease = args.Contains("--release");
var arguments = args.Where(argument => argument != "--release").ToArray();

if (arguments is ["--rules"])
{
    Console.Write(Rules.Table());

    return 0;
}

if (arguments is ["--fleet", .. var rest] && rest.Length > 0)
{
    var report = rest.SkipWhile(argument => argument != "--report").Skip(1).FirstOrDefault();
    var folders = rest.TakeWhile(argument => argument != "--report");
    var repositories = folders.SelectMany(Directory.EnumerateDirectories).Where(IsRepository).Order(StringComparer.Ordinal);
    var fleet = repositories.Select(Repository.At).Select(repository => (repository, Checks.Of(repository, standard, isRelease))).ToList();
    var page = Report.Fleet(fleet);

    if (report is null)
        Console.Write(page);
    else
        File.WriteAllText(report, page);

    Console.Error.WriteLine($"{fleet.Count(row => Report.IsConforming(row.Item2))} of {fleet.Count} repositories conform");

    return 0;
}

if (arguments is not [var root] || !Directory.Exists(root))
{
    Console.Error.WriteLine($"Usage:\n{usage}");

    return 2;
}

var single = Repository.At(root);
var findings = Checks.Of(single, standard, isRelease);

Console.Write(Report.Of(single, findings));

return Report.IsConforming(findings) ? 0 : 1;

static bool IsRepository(string folder) =>
    Path.Exists(Path.Combine(folder, ".git")) && File.Exists(Path.Combine(folder, "global.json")) && !Outside.Names.Contains(Path.GetFileName(folder));

/// <summary>The repositories the fleet keeps that the standard does not ask of.</summary>
internal static class Outside
{
    /// <summary>Snail.Sdk is the standard itself.</summary>
    public static readonly HashSet<string> Names = new(StringComparer.Ordinal) { "Snail.Sdk" };
}

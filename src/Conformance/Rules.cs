using System.Text;

namespace Snail.Sdk.Conformance;

/// <summary>Whether a finding fails the check or is only reported.</summary>
internal enum Severity
{
    Required,
    Advice
}

/// <summary>
/// One check of the standard: the id it keeps from release to release, the name the table shows, whether it fails the
/// build, the repositories it is asked of, and what holds.
/// </summary>
internal sealed record Rule(string Id, string Name, Severity Severity, string AskedOf, string Holds)
{
    /// <summary>The check holds.</summary>
    public Finding Met(string detail = "") => new(this, true, detail);

    /// <summary>The check does not hold, and what was seen.</summary>
    public Finding Unmet(string detail) => new(this, false, detail);
}

/// <summary>One check on one repository: which rule, whether it holds, and what was seen.</summary>
internal sealed record Finding(Rule Rule, bool IsMet, string Detail = "")
{
    /// <summary>The rule's id, S3LCnnnn.</summary>
    public string Id => Rule.Id;

    /// <summary>The rule's name, as the table shows it.</summary>
    public string Check => Rule.Name;

    /// <summary>Whether the rule fails the build.</summary>
    public Severity Severity => Rule.Severity;
}

/// <summary>Every check of the standard, by its id. An id is never reused: a check that goes keeps its number retired.</summary>
internal static class Rules
{
    public static readonly Rule Solution = new("S3LC0001", "one .slnx", Severity.Required, "all",
        "one `.slnx` at the root, no `.sln`");

    public static readonly Rule GlobalJson = new("S3LC0002", "global.json", Severity.Required, "all",
        "the `sdk` section equal in value to Snail.Sdk's own (property order and formatting aside)");

    public static readonly Rule CentralVersions = new("S3LC0003", "central versions", Severity.Required, "all",
        "`Directory.Packages.props` with `ManagePackageVersionsCentrally`, no `Version=` on a `PackageReference` in a project");

    public static readonly Rule EditorConfig = new("S3LC0004", ".editorconfig", Severity.Required, "all",
        "the shared `.editorconfig`, line for line (CRLF or LF alike)");

    public static readonly Rule License = new("S3LC0005", "LICENSE", Severity.Required, "all",
        "a `LICENSE` file at the root");

    public static readonly Rule NuGetConfig = new("S3LC0006", "nuget.config", Severity.Required, "all",
        "`nuget.config` with a `packageSourceMapping`");

    public static readonly Rule OnSnailSdk = new("S3LC0007", "on Snail.Sdk", Severity.Required, "all",
        "every project names a Snail.Sdk level, as its `Sdk` attribute or an `<Sdk>` element");

    public static readonly Rule Tests = new("S3LC0008", "tests", Severity.Required, "all",
        "a test project: `*.Tests`, or `SnailProjectKind` `Tests`");

    public static readonly Rule Benchmarks = new("S3LC0009", "benchmarks", Severity.Required, "all",
        "a benchmark project: `*.Benchmarks`, or `SnailProjectKind` `Benchmarks` (a `Tool` does not count)");

    public static readonly Rule BenchmarkBaseline = new("S3LC0010", "benchmark baseline", Severity.Required, "all",
        "`baseline/*-report.csv` beside a benchmark project — the reports perf.yml compares with");

    public static readonly Rule SharedWorkflows = new("S3LC0011", "shared workflows", Severity.Required, "all",
        "`ci.yml`, `docs.yml`, `perf.yml` call the shared workflows of the right level; `publish.yml` has a job calling the "
        + "shared `lib-publish.yml`/`mcp-publish.yml` without `id-token: write` and a job using the `nuget-push` action with "
        + "it; a library may release with a workflow of its own (`nuget push`) in place of `publish.yml`");

    public static readonly Rule Dependabot = new("S3LC0012", "dependabot", Severity.Required, "all",
        "`.github/dependabot.yml` with `version: 2` and the `github-actions` and `nuget` ecosystems");

    public static readonly Rule Readme = new("S3LC0013", "README", Severity.Required, "all",
        "`README.md` whose first non-blank line is an H1 (`# `)");

    public static readonly Rule Reference = new("S3LC0014", "reference generated", Severity.Required, "all",
        "`docs/reference` committed, or a server's tool pages where `SnailReferenceFolder` writes them");

    public static readonly Rule SiteTheme = new("S3LC0015", "site theme", Severity.Required, "all",
        "no `docs/theme`, `docs/requirements.txt` or `theme:` in `docs/mkdocs.yml` — the site wears the SDK's");

    public static readonly Rule NativeWarnings = new("S3LC0016", "native warnings", Severity.Required, "native",
        "a native repository (a library with a `*.NativeProbe`, a server with `SnailMcpNative` not false) silences no "
        + "trim or AOT warning: no `IL` code in a `NoWarn`, and no `IlcTreatWarningsAsErrors` false unless a workflow of the "
        + "repository names the forgiven warnings in `forgiven-native-warnings` (the shared CI then fails on any other)");

    public static readonly Rule MigrationDebt = new("S3LC0017", "migration debt", Severity.Advice, "all",
        "no `NoWarn` in any project or props file — advice");

    public static readonly Rule PublicApi = new("S3LC0018", "public API written down", Severity.Required, "library",
        "`PublicAPI.Shipped.txt` beside every packed project (not a kind, not `IsPackable` false)");

    public static readonly Rule PackageBaseline = new("S3LC0019", "package baseline", Severity.Required, "library",
        "`SnailPackageBaseline` in `Directory.Build.props` once released: a `PublicAPI.Shipped.txt` lists an API, or a "
        + "`v*` tag exists");

    public static readonly Rule NativeProbe = new("S3LC0020", "native probe", Severity.Required, "library",
        "a `*.NativeProbe` project, or `SnailNotNativeBecause` saying why not");

    public static readonly Rule ServerManifest = new("S3LC0021", "server.json", Severity.Required, "server",
        "`.mcp/server.json` with the current registry `$schema`, a `name` ending in the short name, a `description` of at most 100 characters, a `version` other "
        + "than `0.0.0-dev`, `packages` naming the repository's package, transports matching `SnailMcpTransport` "
        + "(`Both`: `stdio` and `streamable-http`), every variable `SNAIL_MCP_<NAME>_`");

    public static readonly Rule ToolFamilies = new("S3LC0022", "tool families", Severity.Required, "server",
        "`docs/tools.json`");

    public static readonly Rule Contract = new("S3LC0023", "contract and guard rules", Severity.Required, "server",
        "a test through `Snail.Sdk.Mcp.Testing` and one with `ToolGuards`");

    public static readonly Rule DataDirectory = new("S3LC0024", "data directory", Severity.Advice, "server",
        "its folders in the home under the s3l name, through `ServerFolders` — advice");

    public static readonly Rule ContainerImage = new("S3LC0025", "container image", Severity.Required, "server",
        "with `SnailMcpContainer` true: a `Dockerfile`, and a workflow calling the shared `mcp-image.yml`");

    public static readonly Rule ReleaseFeed = new("S3LC0026", "release feed", Severity.Required, "all, on release",
        "only when asked (`--release`, `-p:SnailConformanceRelease=true`): no `LocalPackages` source or mapping in "
        + "`nuget.config`, no `-local` version in `global.json` `msbuild-sdks` or `Directory.Packages.props`");

    public static readonly Rule WorkflowInputs = new("S3LC0027", "workflow inputs", Severity.Required, "all",
        "every call of a shared workflow or action (`orldev/Snail.Sdk/...`) names one Snail.Sdk keeps, passes only inputs "
        + "it declares and every input it requires");

    public static readonly Rule ReleaseTag = new("S3LC0028", "release tag", Severity.Advice, "all",
        "`publish.yml` starts on `tags: ['v[0-9]+.[0-9]+.[0-9]+*']`, which the moving `v1` tag does not match — advice");

    public static readonly Rule JobSecrets = new("S3LC0029", "job secrets", Severity.Required, "server with a launchd job",
        "the job template in `deploy/` names no variable that holds a secret (a `TOKEN`, `KEY`, `SECRET`, `PASSWORD` or "
        + "`CREDENTIALS` in its name): a secret goes into the server's settings file");

    public static readonly Rule ImageVersion = new("S3LC0030", "image version", Severity.Required, "server with an image",
        "every `Dockerfile` the shared `mcp-image.yml` builds takes the version it passes: each stage that runs `dotnet publish` "
        + "declares `ARG VERSION`, with a default for a local build, and publishes with `-p:Version=$VERSION`");

    /// <summary>Every rule, in the order of its id.</summary>
    public static IReadOnlyList<Rule> All { get; } =
    [
        Solution, GlobalJson, CentralVersions, EditorConfig, License, NuGetConfig, OnSnailSdk, Tests, Benchmarks, BenchmarkBaseline,
        SharedWorkflows, Dependabot, Readme, Reference, SiteTheme, NativeWarnings, MigrationDebt, PublicApi, PackageBaseline,
        NativeProbe, ServerManifest, ToolFamilies, Contract, DataDirectory, ContainerImage, ReleaseFeed, WorkflowInputs, ReleaseTag,
        JobSecrets, ImageVersion
    ];

    /// <summary>The rules as a Markdown table: id, check, whom it asks, what holds.</summary>
    public static string Table()
    {
        var table = new StringBuilder();

        table.AppendLine("| Id | Check | Asked of | Fails the build | What holds |");
        table.AppendLine("| --- | --- | --- | :-: | --- |");

        foreach (var rule in All)
            table.AppendLine($"| {rule.Id} | {rule.Name} | {rule.AskedOf} | {(rule.Severity is Severity.Required ? "yes" : "no")} | {rule.Holds} |");

        return table.ToString();
    }
}

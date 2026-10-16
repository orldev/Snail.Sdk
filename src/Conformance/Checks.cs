using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Snail.Sdk.Conformance;

/// <summary>The checks of the standard (docs/conformance.md), each reading files only.</summary>
internal static partial class Checks
{
    /// <summary>The schema of server.json the MCP registry reads today.</summary>
    public const string ServerSchema = "https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json";

    private const string PushAction = ".github/actions/nuget-push";

    private const string VersionTag = "v[0-9]+.[0-9]+.[0-9]+*";

    private const string LocalFeed = "LocalPackages";

    private static readonly string[] Workflows = ["ci.yml", "publish.yml", "docs.yml", "perf.yml"];

    /// <summary>
    /// Every check that applies to the repository's kind. The release feed is asked only on a release: until then the
    /// fleet builds on the local feed.
    /// </summary>
    public static IReadOnlyList<Finding> Of(Repository repository, Standard standard, bool isRelease = false)
    {
        var findings = new List<Finding>
        {
            Solution(repository),
            GlobalJson(repository, standard),
            CentralVersions(repository),
            EditorConfig(repository, standard),
            Required(repository, Rules.License, "LICENSE"),
            NuGetConfig(repository),
            OnSnailSdk(repository),
            Tests(repository),
            Benchmarks(repository),
            BenchmarkBaseline(repository),
            SharedWorkflows(repository),
            WorkflowInputs(repository, standard),
            ReleaseTag(repository),
            Dependabot(repository),
            Readme(repository),
            Reference(repository),
            SiteTheme(repository),
            NativeWarnings(repository),
            MigrationDebt(repository)
        };

        findings.AddRange(repository.Kind is RepositoryKind.Library ? LibraryChecks(repository) : ServerChecks(repository));

        if (isRelease)
            findings.Add(ReleaseFeed(repository));

        return findings;
    }

    private static IEnumerable<Finding> LibraryChecks(Repository repository)
    {
        yield return PublicApi(repository);
        yield return PackageBaseline(repository);
        yield return NativeProbe(repository);
    }

    private static IEnumerable<Finding> ServerChecks(Repository repository)
    {
        yield return ServerManifest.Of(repository);
        yield return Required(repository, Rules.ToolFamilies, "docs/tools.json");
        yield return Contract(repository);
        yield return DataDirectory(repository);
        yield return ContainerImage(repository);
        yield return JobSecrets(repository);
    }

    private static Finding Solution(Repository repository)
    {
        var solutions = Directory.EnumerateFiles(repository.Root, "*.slnx").ToList();
        var legacy = Directory.EnumerateFiles(repository.Root, "*.sln").ToList();

        return solutions.Count == 1 && legacy.Count == 0
            ? Rules.Solution.Met()
            : Rules.Solution.Unmet($"{solutions.Count} .slnx, {legacy.Count} .sln at the root");
    }

    /// <summary>The sdk section compared by value: a copy written with other line endings, spacing or order is the same file.</summary>
    private static Finding GlobalJson(Repository repository, Standard standard)
    {
        if (repository.Json("global.json") is not { ValueKind: JsonValueKind.Object } global || !global.TryGetProperty("sdk", out var sdk))
            return Rules.GlobalJson.Unmet("no global.json with an sdk section");

        if (JsonElement.DeepEquals(sdk, standard.Sdk))
            return Rules.GlobalJson.Met();

        return Rules.GlobalJson.Unmet(string.Join("; ", SdkDifferences(sdk, standard.Sdk)));
    }

    private static IEnumerable<string> SdkDifferences(JsonElement sdk, JsonElement expected)
    {
        if (sdk.ValueKind is not JsonValueKind.Object)
        {
            yield return $"sdk is {Compact(sdk)}, not an object";
            yield break;
        }

        foreach (var property in expected.EnumerateObject())
        {
            if (!sdk.TryGetProperty(property.Name, out var value))
                yield return $"sdk.{property.Name} is missing, the standard's is {Compact(property.Value)}";
            else if (!JsonElement.DeepEquals(value, property.Value))
                yield return $"sdk.{property.Name} is {Compact(value)}, the standard's {Compact(property.Value)}";
        }

        foreach (var property in sdk.EnumerateObject().Where(property => !expected.TryGetProperty(property.Name, out _)))
            yield return $"sdk.{property.Name} is not in the standard";
    }

    private static Finding CentralVersions(Repository repository)
    {
        var isCentral = repository.Text("Directory.Packages.props") is not null
            && $"{repository.Text("Directory.Build.props")}{repository.Text("Directory.Packages.props")}".Contains("<ManagePackageVersionsCentrally>true", StringComparison.Ordinal);
        var pinned = repository.Projects.Where(project => VersionedReference().IsMatch(File.ReadAllText(project))).Select(Path.GetFileName).ToList();

        if (!isCentral)
            return Rules.CentralVersions.Unmet("Directory.Packages.props with ManagePackageVersionsCentrally is missing");

        return pinned.Count == 0
            ? Rules.CentralVersions.Met()
            : Rules.CentralVersions.Unmet($"a version written in {string.Join(", ", pinned)}");
    }

    /// <summary>The shared file line for line: a checkout that turned LF into CRLF holds the same file.</summary>
    private static Finding EditorConfig(Repository repository, Standard standard)
    {
        if (repository.Text(".editorconfig") is not { } text)
            return Rules.EditorConfig.Unmet("missing");

        var lines = Lines(text);
        var expected = Lines(standard.EditorConfig);

        if (lines.SequenceEqual(expected, StringComparer.Ordinal))
            return Rules.EditorConfig.Met();

        var differing = Enumerable.Range(0, Math.Max(lines.Count, expected.Count))
            .First(index => index >= lines.Count || index >= expected.Count || lines[index] != expected[index]);

        return Rules.EditorConfig.Unmet($"differs from the shared file from line {differing + 1}");
    }

    private static Finding NuGetConfig(Repository repository) =>
        repository.Text("nuget.config") is { } text && text.Contains("<packageSourceMapping>", StringComparison.Ordinal)
            ? Rules.NuGetConfig.Met()
            : Rules.NuGetConfig.Unmet("no nuget.config mapping packages to their sources");

    private static Finding OnSnailSdk(Repository repository)
    {
        var elsewhere = repository.Projects.Where(project => !Repository.SdkOf(project).StartsWith("Snail.Sdk", StringComparison.Ordinal)).Select(Path.GetFileName).ToList();

        if (repository.Projects.Count == 0)
            return Rules.OnSnailSdk.Unmet("no project");

        return elsewhere.Count == 0
            ? Rules.OnSnailSdk.Met()
            : Rules.OnSnailSdk.Unmet($"not on a Snail.Sdk level: {string.Join(", ", elsewhere)}");
    }

    private static Finding Tests(Repository repository) =>
        repository.Projects.Any(project => Repository.KindOf(project) == "Tests")
            ? Rules.Tests.Met()
            : Rules.Tests.Unmet("no *.Tests project");

    private static Finding Benchmarks(Repository repository) =>
        BenchmarkProjects(repository).Count > 0
            ? Rules.Benchmarks.Met()
            : Rules.Benchmarks.Unmet("no *.Benchmarks project");

    /// <summary>The reports perf.yml compares with: baseline/ beside the benchmark project, as BenchmarkDotNet writes them.</summary>
    private static Finding BenchmarkBaseline(Repository repository)
    {
        var folders = BenchmarkProjects(repository).Select(project => Path.Combine(Path.GetDirectoryName(project)!, "baseline")).ToList();
        var hasReports = folders.Any(folder => Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*-report.csv").Any());

        if (folders.Count == 0)
            return Rules.BenchmarkBaseline.Unmet("no benchmark project to keep a baseline beside");

        return hasReports
            ? Rules.BenchmarkBaseline.Met()
            : Rules.BenchmarkBaseline.Unmet($"no *-report.csv in {string.Join(", ", folders.Select(folder => Relative(repository, folder)))}: copy the CSV reports of an accepted perf.yml run there");
    }

    private static List<string> BenchmarkProjects(Repository repository) =>
        [.. repository.Projects.Where(project => Repository.KindOf(project) == "Benchmarks")];

    /// <summary>
    /// The shared workflows of the repository's level. publish.yml releases in two jobs: the shared workflow builds and
    /// packs without an OIDC token, and a job of the repository's own pushes through the shared nuget-push action with
    /// one, as nuget.org gives a trusted publishing key only to a job of the workflow file its policy names. A library
    /// may release with a workflow of its own instead.
    /// </summary>
    private static Finding SharedWorkflows(Repository repository)
    {
        var level = repository.Kind is RepositoryKind.Library ? "lib" : "mcp";
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ci.yml"] = $"{level}-ci.yml",
            ["publish.yml"] = $"{level}-publish.yml",
            ["docs.yml"] = "docs.yml",
            ["perf.yml"] = "perf.yml"
        };
        var problems = new List<string>();
        string? ownRelease = null;

        foreach (var file in Workflows)
        {
            var problem = repository.Text($".github/workflows/{file}") is { } text
                ? WorkflowProblem(file, expected[file], Workflow.Parse(text))
                : $"no {file}";

            if (problem is null)
                continue;

            var own = file == "publish.yml" && repository.Kind is RepositoryKind.Library ? OwnRelease(repository) : null;

            if (own is null)
                problems.Add(problem);
            else
                ownRelease = own;
        }

        if (problems.Count > 0)
            return Rules.SharedWorkflows.Unmet(string.Join("; ", problems));

        return ownRelease is null ? Rules.SharedWorkflows.Met() : Rules.SharedWorkflows.Met($"released by its own {ownRelease}");
    }

    /// <summary>What keeps one workflow of the repository from being the shared one's caller; null when nothing does.</summary>
    private static string? WorkflowProblem(string file, string shared, Workflow workflow)
    {
        var calls = workflow.Calls();
        var call = calls.FirstOrDefault(found => found.Target == $".github/workflows/{shared}");

        if (call is null)
            return $"{file} does not call {Workflow.Shared}.github/workflows/{shared}";

        if (file != "publish.yml")
            return null;

        var push = calls.FirstOrDefault(found => found.Target == PushAction);

        if (push is null)
            return $"publish.yml calls {shared} but pushes through no nuget-push job: nuget.org refuses a key to a job of Snail.Sdk's workflows";

        if (workflow.GrantsIdToken(call.Job))
            return $"the release job {call.Job} holds id-token: only the push job needs it";

        return workflow.GrantsIdToken(push.Job) ? null : $"the push job {push.Job} holds no id-token: write, which the nuget-push action signs in with";
    }

    /// <summary>
    /// Every call of a shared workflow or action passes only inputs it takes, and every input it requires: GitHub
    /// refuses any other call when it runs, in a repository that built green.
    /// </summary>
    private static Finding WorkflowInputs(Repository repository, Standard standard)
    {
        var problems = new List<string>();

        foreach (var file in repository.WorkflowFiles())
        {
            var name = Path.GetFileName(file);

            foreach (var call in Workflow.Parse(File.ReadAllText(file)).Calls())
            {
                if (!standard.Shared.TryGetValue(call.Target, out var called))
                {
                    problems.Add($"{name}:{call.Line} calls {call.Target}, which Snail.Sdk does not keep");
                    continue;
                }

                var inputs = called.Inputs(isAction: !call.Target.EndsWith(".yml", StringComparison.Ordinal));
                var unknown = call.With.Where(key => !inputs.Any(input => input.Key == key)).ToList();
                var unpassed = inputs.Where(input => input.IsRequired && !call.With.Contains(input.Key, StringComparer.Ordinal)).Select(input => input.Key).ToList();

                if (unknown.Count > 0)
                    problems.Add($"{name}:{call.Line} passes {string.Join(", ", unknown)}, which {call.Target} no longer takes");

                if (unpassed.Count > 0)
                    problems.Add($"{name}:{call.Line} leaves out {string.Join(", ", unpassed)}, which {call.Target} requires");
            }
        }

        return problems.Count == 0
            ? Rules.WorkflowInputs.Met()
            : Rules.WorkflowInputs.Unmet(string.Join("; ", problems));
    }

    /// <summary>A release starts on a version tag only: v1, the moving tag of the shared workflows' callers, starts nothing.</summary>
    private static Finding ReleaseTag(Repository repository)
    {
        if (repository.Text(".github/workflows/publish.yml") is not { } text)
            return Rules.ReleaseTag.Met("no publish.yml");

        var patterns = Workflow.Parse(text).TagPatterns();

        return patterns.Count == 0 || patterns.All(pattern => pattern == VersionTag)
            ? Rules.ReleaseTag.Met()
            : Rules.ReleaseTag.Unmet($"publish.yml starts on tags {string.Join(", ", patterns)}: write ['{VersionTag}'], which v1 does not match");
    }

    /// <summary>Dependabot keeps both the actions the workflows call and the packages Directory.Packages.props pins current.</summary>
    private static Finding Dependabot(Repository repository)
    {
        if (repository.Text(".github/dependabot.yml") is not { } text)
            return Rules.Dependabot.Unmet("no .github/dependabot.yml");

        var missing = new[] { "version: 2", "package-ecosystem: github-actions", "package-ecosystem: nuget" }
            .Where(line => !text.Contains(line, StringComparison.Ordinal))
            .ToList();

        return missing.Count == 0
            ? Rules.Dependabot.Met()
            : Rules.Dependabot.Unmet($".github/dependabot.yml lacks {string.Join(", ", missing)}");
    }

    /// <summary>A workflow of the repository's own that pushes its packages to NuGet, kept in place of publish.yml.</summary>
    private static string? OwnRelease(Repository repository) =>
        repository.WorkflowFiles().FirstOrDefault(file => File.ReadAllText(file).Contains("nuget push", StringComparison.Ordinal)) is { } workflow
            ? Path.GetFileName(workflow)
            : null;

    /// <summary>The README opens with the repository's title: the site and the package page take it from there.</summary>
    private static Finding Readme(Repository repository)
    {
        if (repository.Text("README.md") is not { } text)
            return Rules.Readme.Unmet("no README.md");

        var first = Lines(text).FirstOrDefault(line => line.Trim().Length > 0) ?? string.Empty;

        return first.StartsWith("# ", StringComparison.Ordinal)
            ? Rules.Readme.Met()
            : Rules.Readme.Unmet(first.Length == 0 ? "README.md is empty" : $"README.md opens with \"{first}\", not an H1 (# Title)");
    }

    /// <summary>A server built as a container (SnailMcpContainer) has a Dockerfile and builds its image through the shared workflow.</summary>
    private static Finding ContainerImage(Repository repository)
    {
        var isContainer = repository.Projects.Any(project => File.ReadAllText(project).Contains("<SnailMcpContainer>true</SnailMcpContainer>", StringComparison.Ordinal));

        if (!isContainer)
            return Rules.ContainerImage.Met("not a container");

        var hasDockerfile = repository.Files("Dockerfile").Any();
        var callsShared = repository.WorkflowFiles().Any(file => File.ReadAllText(file).Contains($"{Workflow.Shared}.github/workflows/mcp-image.yml@", StringComparison.Ordinal));

        return (hasDockerfile, callsShared) switch
        {
            (true, true) => Rules.ContainerImage.Met(),
            (false, _) => Rules.ContainerImage.Unmet("SnailMcpContainer is true, but there is no Dockerfile"),
            _ => Rules.ContainerImage.Unmet("no workflow calls the shared mcp-image.yml")
        };
    }

    /// <summary>The reference committed: docs/reference, or — a server whose docs are a site — wherever SnailReferenceFolder writes the tool pages.</summary>
    private static Finding Reference(Repository repository)
    {
        var folder = Path.Combine(repository.Root, "docs", "reference");
        var docs = Path.Combine(repository.Root, "docs");
        var hasPages = Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.md", SearchOption.AllDirectories).Any();
        var hasToolPages = repository.Kind is RepositoryKind.Server && Directory.Exists(docs)
            && Directory.EnumerateFiles(docs, "tools.json", SearchOption.AllDirectories).Any(file => Path.GetFileName(Path.GetDirectoryName(file)) == "schemas");

        return hasPages || hasToolPages
            ? Rules.Reference.Met()
            : Rules.Reference.Unmet("no docs/reference: build with -p:SnailReference=true and commit it");
    }

    /// <summary>The site wears the theme the SDK carries: a copy kept in docs/ would drift from it, as the four copies did.</summary>
    private static Finding SiteTheme(Repository repository)
    {
        var own = new List<string>();

        if (Directory.Exists(Path.Combine(repository.Root, "docs", "theme")))
            own.Add("docs/theme");

        if (File.Exists(Path.Combine(repository.Root, "docs", "requirements.txt")))
            own.Add("docs/requirements.txt");

        if (repository.Text("docs/mkdocs.yml") is { } configuration && ThemeOfItsOwn().IsMatch(configuration))
            own.Add("a theme in docs/mkdocs.yml");

        return own.Count == 0
            ? Rules.SiteTheme.Met()
            : Rules.SiteTheme.Unmet($"the theme and MkDocs come with the SDK, yet the repository keeps {string.Join(", ", own)}");
    }

    /// <summary>
    /// A native repository hears every trim and AOT warning: one silenced in a NoWarn, or kept from failing the native
    /// publish, is a crash the probe or the server would have caught. The one exception is a native publish kept from
    /// failing while the repository's CI names exactly which warnings it forgives (<c>forgiven-native-warnings</c>, e.g. a
    /// dependency's bridge its programs never cross): the shared CI then still fails on any other native warning.
    /// </summary>
    private static Finding NativeWarnings(Repository repository)
    {
        if (!IsNative(repository))
            return Rules.NativeWarnings.Met("not native");

        var silenced = BuildFiles(repository)
            .Select(file => (File: file, Codes: SilencedCodes(file).Where(IsNativeCode).ToList(), IsLenient: IlcLenient().IsMatch(File.ReadAllText(file))))
            .Where(file => file.Codes.Count > 0 || file.IsLenient)
            .Select(file => $"{Relative(repository, file.File)}: {string.Join(", ", file.Codes.Select(code => $"NoWarn {code}").Concat(file.IsLenient ? ["IlcTreatWarningsAsErrors false"] : []))}")
            .ToList();

        if (silenced.Count == 0)
            return Rules.NativeWarnings.Met();

        var forgiven = ForgivenNativeWarnings(repository);
        var onlyLenient = BuildFiles(repository).All(file => !SilencedCodes(file).Any(IsNativeCode));

        return onlyLenient && forgiven is not null
            ? Rules.NativeWarnings.Met($"the CI forgives only {forgiven}")
            : Rules.NativeWarnings.Unmet(string.Join("; ", silenced));
    }

    /// <summary>The pattern of native warnings a workflow of the repository forgives, or null when none names one.</summary>
    private static string? ForgivenNativeWarnings(Repository repository) =>
        repository.WorkflowFiles()
            .Select(file => ForgivenWarnings().Match(File.ReadAllText(file)))
            .Where(match => match.Success)
            .Select(match => match.Groups["pattern"].Value.Trim())
            .FirstOrDefault(pattern => pattern.Length > 0);

    /// <summary>Every NoWarn of every project and props file — in a native repository, less the trim and AOT codes NativeWarnings fails on.</summary>
    private static Finding MigrationDebt(Repository repository)
    {
        var isNative = IsNative(repository);
        var debt = BuildFiles(repository)
            .Select(file => (File: file, Codes: SilencedCodes(file).Where(code => !isNative || !IsNativeCode(code)).ToList()))
            .Where(file => file.Codes.Count > 0)
            .Select(file => $"NoWarn in {Relative(repository, file.File)}: {string.Join(";", file.Codes)}")
            .ToList();

        return debt.Count == 0
            ? Rules.MigrationDebt.Met()
            : Rules.MigrationDebt.Unmet(string.Join("; ", debt));
    }

    /// <summary>A library proven by a native probe, or a server published with Native AOT (SnailMcpNative, true unless set false).</summary>
    private static bool IsNative(Repository repository)
    {
        if (repository.Kind is RepositoryKind.Library)
            return repository.Projects.Any(project => Repository.KindOf(project) == "NativeProbe");

        return ServerManifest.ServerProjects(repository)
            .Any(project => !string.Equals(Repository.Setting(project, "SnailMcpNative"), "false", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> BuildFiles(Repository repository) =>
        repository.Projects.Concat(repository.Files("*.props")).Order(StringComparer.Ordinal);

    private static IEnumerable<string> SilencedCodes(string file) =>
        NoWarn().Matches(File.ReadAllText(file))
            .SelectMany(match => match.Groups["codes"].Value.Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(code => code != "$(NoWarn)");

    private static bool IsNativeCode(string code) => NativeCode().IsMatch(code);

    private static Finding PublicApi(Repository repository)
    {
        var packed = repository.Projects.Where(Repository.IsPacked);
        var unwritten = packed
            .Where(project => !File.Exists(ShippedApi(project)))
            .Select(Path.GetFileName)
            .ToList();

        return unwritten.Count == 0
            ? Rules.PublicApi.Met()
            : Rules.PublicApi.Unmet($"no PublicAPI.Shipped.txt beside {string.Join(", ", unwritten)}");
    }

    /// <summary>
    /// A released library names the release its package is validated against. Released means a PublicAPI.Shipped.txt
    /// lists an API — which a checkout without tags still shows — or a v* tag exists.
    /// </summary>
    private static Finding PackageBaseline(Repository repository)
    {
        var shipped = repository.Projects.Where(Repository.IsPacked).Where(project => ListsAnApi(ShippedApi(project))).Select(Path.GetFileName).ToList();
        var tag = repository.Tags().FirstOrDefault(name => name.StartsWith('v'));
        var hasBaseline = repository.Text("Directory.Build.props")?.Contains("<SnailPackageBaseline>", StringComparison.Ordinal) == true;

        if (shipped.Count == 0 && tag is null)
            return Rules.PackageBaseline.Met("not released yet");

        if (hasBaseline)
            return Rules.PackageBaseline.Met();

        var released = tag is not null ? $"tagged {tag}" : $"the shipped API of {string.Join(", ", shipped)} is written down";

        return Rules.PackageBaseline.Unmet($"released ({released}), but SnailPackageBaseline names no release to validate against");
    }

    private static string ShippedApi(string project) => Path.Combine(Path.GetDirectoryName(project)!, "PublicAPI.Shipped.txt");

    private static bool ListsAnApi(string file) =>
        File.Exists(file) && File.ReadLines(file).Any(line => line.Trim() is { Length: > 0 } entry && !entry.StartsWith('#'));

    /// <summary>A probe proving the library under Native AOT, or the reason it is not native, said in SnailNotNativeBecause.</summary>
    private static Finding NativeProbe(Repository repository)
    {
        var hasProbe = repository.Projects.Any(project => Repository.KindOf(project) == "NativeProbe");
        var buildFiles = repository.Projects.Append(Path.Combine(repository.Root, "Directory.Build.props")).Where(File.Exists);
        var declarations = buildFiles.Select(file => SnailNotNative().Match(File.ReadAllText(file)));
        var declared = declarations.FirstOrDefault(match => match.Success);

        if (hasProbe)
            return Rules.NativeProbe.Met();

        return declared is not null
            ? Rules.NativeProbe.Met($"not native: {declared.Groups["reason"].Value.Trim()}")
            : Rules.NativeProbe.Unmet("no *.NativeProbe project proving the library under Native AOT, and no SnailNotNativeBecause saying why not");
    }

    private static Finding Contract(Repository repository)
    {
        var tests = repository.Projects.Where(project => Repository.KindOf(project) == "Tests").Select(Path.GetDirectoryName).OfType<string>().ToList();
        var sources = tests.SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)).Select(File.ReadAllText).ToList();
        var hasContract = sources.Any(source => source.Contains("ServerProgram.", StringComparison.Ordinal));
        var hasGuards = sources.Any(source => source.Contains("ToolGuards.Problems", StringComparison.Ordinal));

        return (hasContract, hasGuards) switch
        {
            (true, true) => Rules.Contract.Met(),
            (false, _) => Rules.Contract.Unmet("no test starts the server through Snail.Sdk.Mcp.Testing"),
            _ => Rules.Contract.Unmet("no test checks the listed tools with ToolGuards")
        };
    }

    /// <summary>
    /// A server's folders in the user's home carry its s3l name: named through ServerFolders, or spelled out. A server
    /// that keeps no folder there at all names neither, and holds too.
    /// </summary>
    private static Finding DataDirectory(Repository repository)
    {
        var name = repository.ShortName;
        var sources = repository.Files("*.cs").Select(File.ReadAllText).ToList();
        var isNamed = sources.Any(text => text.Contains("ServerFolders.", StringComparison.Ordinal) || text.Contains($".s3l-mcp-{name}", StringComparison.Ordinal));
        var keepsOldName = sources.Any(text => text.Contains($".snail-mcp-{name}\"", StringComparison.Ordinal) || text.Contains($"\".snail-mcp-{name}", StringComparison.Ordinal));

        return isNamed || !keepsOldName
            ? Rules.DataDirectory.Met()
            : Rules.DataDirectory.Unmet($"~/.snail-mcp-{name} is still named: ServerFolders.DataDirectory(\"{name}\") gives ~/.s3l-mcp-{name} and moves the old folder there");
    }

    /// <summary>
    /// A launchd job template names no variable holding a secret. The template is in the repository, and the job
    /// installed from it is restarted by an install only while it is the job written; a secret goes into the server's
    /// settings file, which <c>ServerSettings</c> reads, and never into either.
    /// </summary>
    private static Finding JobSecrets(Repository repository)
    {
        var secrets = repository.Files("*.plist")
            .Where(file => Path.GetFileName(Path.GetDirectoryName(file)) == "deploy")
            .SelectMany(file => Variables(File.ReadAllText(file)).Where(IsSecret).Select(variable => $"{Relative(repository, file)}: {variable}"))
            .ToList();

        return secrets.Count == 0
            ? Rules.JobSecrets.Met()
            : Rules.JobSecrets.Unmet($"{string.Join(", ", secrets)} — move it into ~/.config/s3l-mcp-{repository.ShortName}/config.json, which the server reads itself");
    }

    /// <summary>The names under the job's EnvironmentVariables.</summary>
    private static IEnumerable<string> Variables(string plist)
    {
        var keys = Xml(plist)?.Descendants("key").ToList() ?? [];
        var environment = keys.FirstOrDefault(key => key.Value == "EnvironmentVariables")?.ElementsAfterSelf().FirstOrDefault();

        return environment?.Name == "dict" ? environment.Elements("key").Select(key => key.Value.Trim()) : [];
    }

    private static bool IsSecret(string variable) =>
        variable.Split('_', StringSplitOptions.RemoveEmptyEntries).Any(word => SecretWords.Contains(word, StringComparer.OrdinalIgnoreCase));

    private static readonly string[] SecretWords = ["TOKEN", "KEY", "APIKEY", "SECRET", "PASSWORD", "PASSWD", "CREDENTIALS"];

    /// <summary>
    /// Nothing of the local feed is left for a release: the source and its mapping in nuget.config, and the -local
    /// versions it holds, named in global.json's msbuild-sdks or in Directory.Packages.props.
    /// </summary>
    private static Finding ReleaseFeed(Repository repository)
    {
        var leftovers = new List<string>();

        if (repository.Text("nuget.config") is { } configuration && Xml(configuration) is { } feeds)
        {
            var sources = feeds.Descendants("packageSources").Elements("add").Any(source => (string?)source.Attribute("key") == LocalFeed);
            var mapping = feeds.Descendants("packageSourceMapping").Elements("packageSource").Any(source => (string?)source.Attribute("key") == LocalFeed);

            if (sources)
                leftovers.Add($"nuget.config adds the source {LocalFeed}");

            if (mapping)
                leftovers.Add($"nuget.config maps packages to {LocalFeed}");
        }

        if (repository.Json("global.json") is { ValueKind: JsonValueKind.Object } global
            && global.TryGetProperty("msbuild-sdks", out var sdks) && sdks.ValueKind is JsonValueKind.Object)
        {
            leftovers.AddRange(sdks.EnumerateObject()
                .Where(sdk => sdk.Value.ValueKind is JsonValueKind.String && IsLocalVersion(sdk.Value.GetString()!))
                .Select(sdk => $"global.json takes {sdk.Name} {sdk.Value.GetString()}"));
        }

        if (repository.Text("Directory.Packages.props") is { } packages && Xml(packages) is { } versions)
        {
            leftovers.AddRange(versions.Descendants("PackageVersion")
                .Where(package => IsLocalVersion((string?)package.Attribute("Version") ?? string.Empty))
                .Select(package => $"Directory.Packages.props takes {(string?)package.Attribute("Include")} {(string?)package.Attribute("Version")}"));
        }

        return leftovers.Count == 0
            ? Rules.ReleaseFeed.Met()
            : Rules.ReleaseFeed.Unmet(string.Join("; ", leftovers));
    }

    private static bool IsLocalVersion(string version) => version.Contains("-local", StringComparison.OrdinalIgnoreCase);

    private static XDocument? Xml(string text)
    {
        try
        {
            return XDocument.Parse(text);
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static Finding Required(Repository repository, Rule rule, string path) =>
        repository.Text(path) is not null
            ? rule.Met()
            : rule.Unmet($"no {path}");

    /// <summary>A text's lines, whatever line endings it was written or checked out with.</summary>
    private static List<string> Lines(string text) =>
        [.. text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n')];

    private static string Relative(Repository repository, string path) =>
        Path.GetRelativePath(repository.Root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);

    [GeneratedRegex(@"<PackageReference\s[^>]*\bVersion=""", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedReference();

    [GeneratedRegex(@"<NoWarn>(?<codes>[^<]*)</NoWarn>", RegexOptions.CultureInvariant)]
    private static partial Regex NoWarn();

    [GeneratedRegex(@"^IL\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex NativeCode();

    [GeneratedRegex(@"<IlcTreatWarningsAsErrors>\s*false\s*</IlcTreatWarningsAsErrors>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IlcLenient();

    [GeneratedRegex(@"^\s*forgiven-native-warnings:\s*['""]?(?<pattern>[^'""\r\n#]+)", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex ForgivenWarnings();

    [GeneratedRegex(@"<SnailNotNativeBecause>(?<reason>[^<]+)</SnailNotNativeBecause>", RegexOptions.CultureInvariant)]
    private static partial Regex SnailNotNative();

    [GeneratedRegex(@"^theme:", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ThemeOfItsOwn();
}

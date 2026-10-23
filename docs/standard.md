# The Snail standard

What every Snail library and every Snail MCP server looks like, how it is built, tested, documented and released, and
why. Snail.Sdk carries the standard as packages — the MSBuild SDKs, the analyzers, the server libraries, the templates —
together with the shared workflows and the conformance check that says whether a repository follows it. A rule changed
here reaches every project with a version bump instead of a copy, and a repository that wanders off fails its own build.

## What Snail.Sdk is

| Package | For | What it brings |
| --- | --- | --- |
| `Snail.Sdk` | every project | the build rules, every analyzer rule of the .NET SDK as advice and the agreed ones as warnings, `Snail.Sdk.Analyzers`, the threading analyzers, the package audit, deterministic source-linked builds, the project kinds, the conformance check |
| `Snail.Sdk.Library` | a library | `Snail.Sdk`, plus native compatibility, the public API files, package validation against the last release, symbol packages, the API reference, the site |
| `Snail.Sdk.Mcp` | an MCP server and its tests | `Snail.Sdk`, plus the dotnet tool of the McpServer kind, Native AOT per platform, the libraries below implicitly, the tool reference, the site, the local install |
| `Snail.Sdk.Mcp.Hosting` | a server, at run time | the empty host with log lines on stderr, the JSON options of the tools, settings from prefixed variables, the server's folders, its name and version, the stable path of the running program |
| `Snail.Sdk.Mcp.Http` | a server shared over HTTP, at run time | the loopback web host (for a container image, the published port on every interface), Streamable HTTP with a session per client, the refusal of browser requests |
| `Snail.Sdk.Mcp.Testing` | a server's tests | the server started as a client starts it, with a home folder of its own; `ToolGuards` |
| `Snail.Sdk.Analyzers` | every project, through `Snail.Sdk` | the rules no .NET analyzer has ([the rules](rules/index.md)) |
| `Snail.Sdk.Templates` | people starting a project | `dotnet new s3l-lib`, `dotnet new s3l-mcp`, `dotnet new s3l-web` |

A project names one level in its `Sdk` attribute, and the version once, in `global.json`:

```xml
<Project Sdk="Snail.Sdk.Library">      <!-- a library -->
<Project Sdk="Snail.Sdk.Mcp">          <!-- a server, and its test project -->
<Project Sdk="Snail.Sdk">              <!-- a library's tests, benchmarks, native probe -->
```

```json
"msbuild-sdks": { "Snail.Sdk": "0.1.0", "Snail.Sdk.Library": "0.1.0" }
```

Every package is released together, at one version. A level references the analyzers and the server libraries of its
own release (`SnailSdkVersion`, written into the package when it is packed), so an SDK never meets a host library or an
analyzer it was not built and tested with.

## Names

`s3l` is the short name of Snail. Public identities — what other people and registries already refer to — keep the
long one, since renaming them breaks whoever refers to them. Names typed on a machine by people and scripts use the
short one.

| What | Name | Example |
| --- | --- | --- |
| Repository, NuGet package | `Snail.Toolkit.<Name>`, `Snail.MCP.<Name>`, `Snail.Sdk.<Level>` | `Snail.MCP.Memory` |
| MCP registry entry (`.mcp/server.json`) | `io.github.orldev/snail-mcp-<short>` | `io.github.orldev/snail-mcp-memory` |
| Environment variables of a server | `SNAIL_MCP_<SHORT>_…` | `SNAIL_MCP_MEMORY_DATA_DIRECTORY` |
| Contract test variable | `SNAIL_<SHORT>_SERVER` | `SNAIL_MEMORY_SERVER` |
| MSBuild properties, targets, items | `Snail…` | `SnailMcpNative` |
| Templates | `s3l-lib`, `s3l-mcp`, `s3l-web` | `dotnet new s3l-mcp -n Snail.MCP.Foo` |
| dotnet tool command, name in `initialize` | `s3l-mcp-<short>` | `s3l-mcp-memory` |
| Published programs | `~/.s3l-mcp/<Package>/current` | `~/.s3l-mcp/Snail.MCP.Memory/current` |
| Data of a server | `~/.s3l-mcp-<short>` | `~/.s3l-mcp-memory` |
| Settings of a server | `~/.config/s3l-mcp-<short>/` | `~/.config/s3l-mcp-memory/config.json` |
| launchd job | `s3l.mcp.<short>` | `s3l.mcp.memory` |
| Container image | `ghcr.io/<owner>/s3l-<short>-mcp` | `ghcr.io/orldev/s3l-memory-mcp` |
| Analyzer, conformance, docs tool IDs | `S3L0001`, `S3LS0001`, `S3LC0001`, `S3LD0001` | |

**The short name** of a server is its package id with a leading `Snail.MCP.` removed (in any letter case), every dot
removed, in lower case: `Snail.MCP.CodeContext` is `codecontext`. The build, the template, the image workflow and the
conformance check apply this one rule, so the command, the job, the image and the variables of a server always agree.

**Environment variables keep `SNAIL_MCP_`**: they are a public contract. `server.json` publishes them to the MCP
registry, compose files and job definitions set them, and every registration on every machine names them; a rename
would cost a migration everywhere for a shorter word. Every server reads its settings this way, under its own prefix
only, so an unrelated variable can never become a setting by sharing a name, and over its settings file,
`~/.config/s3l-mcp-<short>/config.json`, where a setting of one machine — a token above all — lives
(`ServerSettings` of `Snail.Sdk.Mcp.Hosting`, which both host builders apply by themselves).

**The MSBuild prefix stays `Snail`**: MSBuild has one flat namespace of properties, and the prefix is what keeps the
SDK's names apart from Microsoft's and a project's own ([the properties](properties.md)).

## Repository layout

Every repository is one solution with the same files at its root, so that a person, a workflow and the conformance
check find everything in the same place. The templates write exactly these trees.

A library, `dotnet new s3l-lib -n Snail.Toolkit.Foo`:

```
Snail.Toolkit.Foo/
  Snail.Toolkit.Foo.slnx
  global.json                     the sdk section of the standard; msbuild-sdks names the Snail.Sdk version
  nuget.config                    packageSourceMapping
  Directory.Build.props           TargetFramework, central versions, version prefix, repository URL
  Directory.Packages.props        every package version, once
  .editorconfig                   the shared file, line for line
  .gitignore
  LICENSE
  README.md                       opens with "# Snail.Toolkit.Foo"
  src/
    Snail.Toolkit.Foo.csproj      <Project Sdk="Snail.Sdk.Library">
    PublicAPI.Shipped.txt
    PublicAPI.Unshipped.txt
    *.cs
  tests/
    Snail.Toolkit.Foo.Tests/            <Project Sdk="Snail.Sdk">
    Snail.Toolkit.Foo.NativeProbe/      <Project Sdk="Snail.Sdk">
  benchmarks/
    Snail.Toolkit.Foo.Benchmarks/       <Project Sdk="Snail.Sdk">
      baseline/*-report.csv
  docs/
    reference/Snail.Toolkit.Foo.md      written by the build
  .github/
    dependabot.yml
    workflows/ci.yml  docs.yml  perf.yml  publish.yml
```

A server, `dotnet new s3l-mcp -n Snail.MCP.Foo`:

```
Snail.MCP.Foo/
  Snail.MCP.Foo.slnx
  global.json  nuget.config  Directory.Build.props  Directory.Packages.props
  .editorconfig  .gitignore  LICENSE  README.md
  .mcp/server.json                the MCP registry manifest
  src/
    Snail.MCP.Foo.csproj          <Project Sdk="Snail.Sdk.Mcp">
    Program.cs                    one line: ServerHost.RunAsync(args)
    Extensions/                   ServerHost, ServerRegistration (+ HttpServerHost, ServerSettings with HTTP)
    Tools/                        the tool classes
  tests/
    Snail.MCP.Foo.Tests.csproj    <Project Sdk="Snail.Sdk.Mcp">
    StdioContractTests.cs         with stdio
    HttpContractTests.cs          with HTTP
  benchmarks/
    Snail.MCP.Foo.Benchmarks.csproj
    baseline/*-report.csv
  docs/
    tools.json                    the tool families
    reference/tools/              written by the build: index.md, a page per family
    reference/schemas/tools.json
  deploy/s3l.mcp.foo.plist        with HTTP: the launchd job template
  Dockerfile  .dockerignore       with --container true
  .github/
    dependabot.yml
    workflows/ci.yml  docs.yml  perf.yml  publish.yml  image.yml (with --container true)
```

An ASP.NET application, `dotnet new s3l-web -n Vertical.Foo`. It publishes no package and no API reference: it is
released as its image, and the conformance check holds it to that as an application (a repository whose own project
builds on the Web SDK).

```
Vertical.Foo/
  Vertical.Foo.slnx
  global.json  nuget.config  Directory.Build.props  Directory.Packages.props
  .editorconfig  .gitignore  .dockerignore  LICENSE  README.md
  src/
    Vertical.Foo.csproj           <Project Sdk="Microsoft.NET.Sdk.Web"> with <Sdk Name="Snail.Sdk" />
    Program.cs                    which features are on: AddApplication, UseApplication, MapApplication
    Extensions/                   the composition root
    Proxy/  RateLimiting/  Health/      the X-Forwarded headers of the configured proxy, the limit per client, /health
    Greetings/  or  Components/   the minimal API, or Blazor Server pages styled with Tailwind (--kind blazor)
    Account/  Storage/  Messaging/      with --auth, --mongo, --rabbit
  tests/
    Vertical.Foo.Tests.csproj     WebApplicationFactory<Program>
  benchmarks/
    Vertical.Foo.Benchmarks.csproj
    baseline/*-report.csv
  Dockerfile                      tini as process 1; Node in the build stage for Tailwind
  docker-compose.yml  or  deploy/nomad/vertical-foo.nomad.hcl   behind traefik (--deploy)
  .github/
    dependabot.yml
    workflows/ci.yml  image.yml  perf.yml
```

What each root file holds, and why:

| File | Holds | Why |
| --- | --- | --- |
| `*.slnx` | one solution, no `.sln` | the workflows build the one solution at the root without being told its name |
| `global.json` | the `sdk` section of Snail.Sdk's own (equal in value); `msbuild-sdks` with the Snail.Sdk version | the code style analyzers ship with the .NET SDK, so a machine on another feature band would judge the same sources differently; `rollForward: latestPatch` takes fixes and refuses the next band |
| `nuget.config` | a `packageSourceMapping` | with central package management and more than one source NuGet requires it (NU1507), and it stops a public package from shadowing a private one of the same name |
| `Directory.Packages.props` | `ManagePackageVersionsCentrally` and every version | one version of each package per repository, raised in one place by Dependabot |
| `Directory.Build.props` | what is the repository's own: target framework, version prefix, repository URL, `SnailPackageBaseline` | the rules are the SDK's, so nothing here repeats them |
| `.editorconfig` | formatting only: charset, line ends, indentation | an `.editorconfig` outranks the SDK's global config, so a rule kept here would freeze the copy and shadow the next release of the SDK |
| `README.md` | an H1 first | it is the package's README and the site's home page; the H1 is the site's title |
| `LICENSE` | the licence | every repository is published |

A repository with several projects keeps them in `src/<Project>/`, `tests/<Project>/`. A single test or benchmark
project may sit directly in `tests/` or `benchmarks/`; a test project there leaves `tests/<Name>.NativeProbe/` to the
probe, which the SDK keeps out of the test project's files. The benchmark baseline sits in `baseline/` beside the
benchmark project, where `perf.yml` looks for it.

## The build

### What every project gets

`Snail.Sdk` sets two kinds of property before the project body, so a project can still say otherwise. A **default** is
set only when nothing above it — a repository's `Directory.Build.props` — has set it. A **rule** of the standard is set
whatever `Directory.Build.props` says, because a whole repository does not opt out of the standard in one line; a
project body can still override it, and the conformance check reports such lines as migration debt.

| Property | Value | Kind | Why |
| --- | --- | --- | --- |
| `LangVersion` | `latest` | default | the newest C# of the pinned SDK band |
| `ImplicitUsings` | `enable` | default | |
| `Nullable` | `enable` | rule | a nullable return means "unknown" and the caller must answer it; the reference pages print the annotations |
| `GenerateDocumentationFile` | `true` | rule | every public member is documented (CS1591), and the API reference reads the XML |
| `TreatWarningsAsErrors` | `true` | rule | the agreed rules and the audit fail the build only through it |
| `EnforceCodeStyleInBuild` | `true` | rule | code style is judged by the build, not only by an editor |
| `AnalysisLevel`, `AnalysisMode` | `latest`, `Default` | rule | see [Code style](#code-style) |
| `NuGetAudit`, `NuGetAuditMode`, `NuGetAuditLevel` | `true`, `all`, `low` | rule | a known vulnerability in any package, direct or transitive, fails the build; live with one advisory by naming it, `<NuGetAuditSuppress Include="<advisory URL>" />`, not by lowering the level |
| `Deterministic`, `PublishRepositoryUrl`, `EmbedUntrackedSources` | `true` | rule | the same sources give the same binaries, and a package says which commit it was built from |
| `ContinuousIntegrationBuild` | `true` on CI (`GITHUB_ACTIONS` or `CI`) | rule | no machine's folder is written into a package |
| `Authors`, `PackageLicenseExpression` | `orldev`, `MIT` | default | |

It references `Snail.Sdk.Analyzers` of its own release (unless `SnailAnalyzers` is `false`) and
`Microsoft.VisualStudio.Threading.Analyzers` (`SnailThreadingAnalyzersVersion`, default 17.14.15), and adds the target
`SnailConformance` ([Conformance](#conformance)). The target framework stays in each repository: set by the SDK, it would
turn a multi-targeted project into a single-target one.

### Project kinds

A project's kind decides what it is spared and whether it ships. Three kinds are read from the name, so a test project
needs no lines of its own; the fourth is set in the project body or a `Directory.Build.props` above it
(`<SnailProjectKind>Tool</SnailProjectKind>`), and a name ending otherwise can set any kind the same way.

| Kind | Recognised by | What it gets | Why |
| --- | --- | --- | --- |
| *(none)* | any other name | everything below for its level; packed | the library or the server itself |
| `Tests` | `*.Tests` | not packed; no CS1591, CA1707; `Snail.Sdk.Tests.globalconfig` (no async suffix, `Subject_Condition_ExpectedResult` names, no CA1001); its strings are data to S3L0005 | test names read as sentences and their text is input, not prose |
| `Benchmarks` | `*.Benchmarks` | an `Exe`; not packed; the same relaxations as `Tests` | BenchmarkDotNet's conventions |
| `NativeProbe` | `*.NativeProbe` | an `Exe` published with Native AOT and invariant globalization; not packed; no CS1591, CA1707 | the app that proves a library works when published natively |
| `Tool` | set explicitly | an `Exe`; not packed; held to every rule of the code beside it | a generator or a probe of the repository's own is code like any other, and never counts as a test or a benchmark |

A project on another Microsoft SDK (`Microsoft.NET.Sdk.Razor`, `Microsoft.NET.Sdk.Web`) keeps it in its `Sdk` attribute and
adds the Snail level as its first element, `<Sdk Name="Snail.Sdk.Library" />`. Snail's targets then come after
Microsoft's and make up for what those had already decided: the test relaxations reach the compiler, and a native library
is still checked by the trim and AOT analyzers.

### Snail.Sdk.Library

| Adds | Why |
| --- | --- |
| `IsAotCompatible` true, unless `SnailNotNativeBecause` says why not | a library that cannot be native says so in one sentence; the trim and AOT analyzers check every other one |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`SnailPublicApiAnalyzersVersion`, default 5.6.0) with RS0016, RS0017, RS0022, RS0024–RS0027, RS0036, RS0037 as warnings | a change to the public API shows in the diff of `PublicAPI.*.txt` and is a decision, not an accident |
| `EnablePackageValidation`, with `PackageValidationBaselineVersion` from `SnailPackageBaseline` | a breaking change against the last release fails the build |
| the version prefix from `SnailPackageBaseline` when no version is given | a local pack is never below the baseline it is validated against |
| `IncludeSymbols`, `.snupkg` (not for a package without an assembly) | a debugger reaches the source through the symbols and SourceLink |
| `-p:SnailReference=true` writes `<assembly>.md` into `SnailReferenceFolder` (default `docs/reference` beside `global.json`) | [Documentation and the site](#documentation-and-the-site) |

The kinds get none of these: tests, benchmarks and probes have no public API to keep.

### Snail.Sdk.Mcp

A server is described by three properties; everything else follows from them. Each is a default, so a
`Directory.Build.props` may set it for every project of the repository.

| Property | Values | Default | Sets |
| --- | --- | --- | --- |
| `SnailMcpNative` | `true`, `false` | `true` | Native AOT wherever it is published for a runtime, size optimisation, the configuration binding generator (and the request delegate generator with HTTP), a tool package per platform; `false` for a server that loads code at run time (Roslyn, MSBuild, Razor, Playwright) |
| `SnailMcpTransport` | `Stdio`, `Http`, `Both` | `Stdio` | `Http` and `Both` reference `Snail.Sdk.Mcp.Http` and use the launchd job template in `deploy/s3l.mcp.<short>.plist` when it exists |
| `SnailMcpContainer` | `true`, `false` | `false` | the repository builds an image from its `Dockerfile`; a publish without a runtime stays framework-dependent, as a Dockerfile's build needs |

And for the server project (no kind):

- `OutputType Exe`, `PackAsTool`, `PackageType McpServer`, the command `s3l-mcp-<short>` (`ToolCommandName`), and
  `README.md` and `.mcp/server.json` packed — the package NuGet, `dotnet tool install`, `dnx` and the MCP registry
  read.
- The command written into the assembly as `Snail.Sdk.Mcp.ServerName`: `ServerIdentity.Of(assembly)` reads it with the
  version, so a server sets `ServerInfo` in one line and a client knows it by the name a person types.
- `ToolPackageRuntimeIdentifiers` of a native server: `osx-arm64;linux-x64;linux-arm64;win-x64;any` — a native
  package for each platform, built and asked over stdio on that platform by the shared workflows, and a
  framework-dependent fallback (`any`) for every other. A server whose code runs on fewer systems sets its own list.
- `InvariantGlobalization` and `RollForward LatestMajor` (defaults): a server needs no ICU on the machine it runs on and
  formats nothing by culture, and a framework-dependent tool still starts on a machine that has only a newer .NET.
- The workstation GC (`ServerGarbageCollection false`, the Web SDK's `true` replaced): a server answers one client at a
  time, and the server GC's heaps per core nearly doubled one's memory (Snail.MCP.Roslyn idle with a solution loaded,
  283 MB against 481 MB). The concurrent GC stays on. A server that wants the server GC sets it in its project file.
- Without profile-guided tiering when framework-dependent (`TieredPGO false` for `SnailMcpNative false`): measured
  on Snail.MCP.Roslyn 281 → 259 MB at rest and a quarter less CPU, on Snail.MCP.CodeContext 793 → 759 MB and a faster
  index, none slower. A native server has no JIT. A server that wants it sets `TieredPGO` in its project file.
- The heap compacted when the server goes quiet: `McpHost.CreateBuilder` and `McpHttpHost.CreateBuilder` add the idle
  trim, which, once no MCP message has come for five minutes and the process has allocated 32 MB since the last trim,
  runs one compacting collection of every generation — at most once per quiet period. A burst of work leaves its
  garbage in generation 2, which an idle server never collects: Snail.MCP.CleanUp sat at 244 MB after a census, 72 MB
  with the trim, for a pause of about 50 ms. `SNAIL_MCP_IDLE_TRIM` sets the period in seconds and 0 turns it off
  ([installing](installing.md#the-idle-trim)).
- A publication carries the program, not its debug symbols or XML docs.
- `Snail.Sdk.Mcp.Hosting` implicitly, `Snail.Sdk.Mcp.Http` with HTTP; a test project on `Snail.Sdk.Mcp` gets
  `Snail.Sdk.Mcp.Testing` instead.
- `-p:SnailReference=true` writes the tool reference ([Documentation and the site](#documentation-and-the-site)).
- `dotnet publish -c Release -r <rid> -p:SnailMcpInstall=true` installs the server into
  `~/.s3l-mcp/<name>/<date>-<time>.<commit>/` and points `current` at it ([installing](installing.md)).

Every public property, its default and its effect: [the properties](properties.md).

### The image of a container server

The `Dockerfile` is the repository's own (`dotnet new s3l-mcp --container true` writes one); the build and the tags are
the shared `mcp-image.yml`. Its build stage names the band of `global.json` (`mcr.microsoft.com/dotnet/sdk:10.0.302`),
never the moving `sdk:10.0`, which would one day bring a band the build refuses. A native server publishes with Native
AOT for `linux-x64` — `clang` and `zlib1g-dev` in the build stage — onto `mcr.microsoft.com/dotnet/runtime-deps:10.0`:
no .NET runtime in the image, a smaller image and a faster start. Any other server publishes framework-dependent onto
`mcr.microsoft.com/dotnet/aspnet:10.0`. Both run as the image's `app` user. `McpHttpHost.CreateBuilder` listens on loopback only,
which a container's published port does not reach, so a server that serves HTTP from an image builds its host with
`McpHttpHost.CreateContainerBuilder(args, port)`: the port on every interface, the published port the boundary, and
everything else the same — the settings, stderr, the idle trim, the refusal of a browser's request by `MapLocalMcp`. A
host of the server's own loses that refusal (Snail.MCP.Documents, 2026-10-16: a request with `Origin` was answered 200).

Images go to GitHub's registry only, `ghcr.io/<owner>/s3l-<short>-mcp`: the run's own token signs in, the image sits
beside its repository, and an anonymous pull meets no rate limit. Docker Hub is used only to pull a test image a
library's tests need. An image says what it is from the inside: the `Dockerfile` takes `ARG VERSION=0.0.0-local` and
builds it in as `-p:Version`, so a local `docker build` — tagged `:local` by hand, never left to default to `latest` —
reports `0.0.0-local` in `initialize`, and `mcp-image.yml` passes the tag's version or `0.0.0-<branch>.<commit>`
([workflows](workflows.md#mcp-imageyml)).

## Code style

The rules of how C# is written are [the guidelines](guidelines.md), shipped with the SDK. They are checked in layers,
each doing what the one before cannot:

| Layer | Checks | Lives in |
| --- | --- | --- |
| `.editorconfig` | formatting: charset, line ends, indentation | each repository, the shared file line for line |
| the .NET SDK's analyzers, as advice | every rule the SDK knows, including those it ships disabled and those a newer SDK adds | `AnalysisLevel=latest`, the SDK's `All` listing turned into suggestions |
| `Snail.Sdk.globalconfig` | the agreed rules as warnings — errors under warnings as errors | the `Snail.Sdk` package |
| `Snail.Sdk.Analyzers` | the guidelines no .NET analyzer checks | a Roslyn analyzer package `Snail.Sdk` references |
| the guidelines | what no analyzer can judge — names that say what a thing is, layering, failure as data | [guidelines.md](guidelines.md), read by people and agents |

**Advice, then rules.** `AnalysisLevel=latest` takes the rules of whichever SDK builds the code, so moving `global.json`
to the next band brings that band's rules. The `Default` mode is kept, not `Recommended`: `Recommended` names some rules
one by one as warnings, and a rule named by itself outranks any category, so a newer SDK's first such rule would fail
the build. A category setting only changes rules a package enables by default, so the rules shipped disabled would stay
unseen. So the build reads the SDK's own listing of every rule of its analysis level (the `All` mode), makes each
warning and error a suggestion, and passes it to the compiler between the SDK's config and `Snail.Sdk.globalconfig`:
every rule shows as a suggestion in the editor and in the build's log the day `global.json` moves, and only the agreed
rules fail the build. Advice becomes a rule by a release of Snail.Sdk. `SnailEveryRuleAsAdvice=false` turns the listing
off.

**The agreed rules** in `Snail.Sdk.globalconfig`:

| Group | Rules | Why |
| --- | --- | --- |
| structure | file-scoped namespaces matching the folders, usings outside and none unused, formatting (IDE0161, IDE0065, IDE0005, IDE0130, IDE0055) | one shape of file everywhere |
| syntax | collection expressions (IDE0028, IDE0300–IDE0305), primary constructors (IDE0290), no `this.` (IDE0003), keywords for types (IDE0049), accessibility written (IDE0040), readonly fields (IDE0044) | the guidelines' modern, minimal C# |
| naming | IDE1006: `_camelCase` private fields, PascalCase members, constants and static readonly fields, `I` interfaces, camelCase locals and parameters, `Async` on async methods | |
| strings | CA1307, CA1309, CA1310: every comparison states its `StringComparison` | culture-dependent comparison is a bug that shows only on another machine |
| performance | CA1869 (cached `JsonSerializerOptions`), CA1870 (`SearchValues`), CA1851 (one enumeration), CA1848 (`LoggerMessage`), CA1852 (sealed internal types), SYSLIB1045 (`GeneratedRegex`) | each has a measured gain; `LogDebug` with plain arguments pays for them with Debug off |
| reliability | CA2213 (owned fields disposed), VSTHRD002, VSTHRD100, VSTHRD101, VSTHRD103, VSTHRD110 (no `.Result`, no `async void`, results observed) | |
| Snail.Sdk.Analyzers | S3L0001–S3L0010 | below |

Some rules are off on purpose — CA2007, CA1062, CA1822 and others that argue with the guidelines or measure nothing
here — and IDE0003 and IDE0049, which a command-line build does not run even when named, are carried by S3L0009 and
S3L0010. CA2000 stays advice: its false positives would bury the rest. The Visual Studio main-thread rules (VSTHRD001,
VSTHRD004, VSTHRD010, VSTHRD012) are off too: a server has no UI thread to break them on, and an analyzer whose every
rule is off is not run — VSTHRD010 alone took 0.33–0.47 s per project, 15–19 % of all analyzer time (measured
2026-10-17 with `-p:ReportAnalyzer=true`), to find nothing.

**The rules of `Snail.Sdk.Analyzers`**, each with a page saying what it reports, why and how to fix it:

| Rule | Id | Reports | Code fix |
| --- | --- | --- | --- |
| [Unpack the query](rules/S3L0001.md) | S3L0001 | a LINQ chain of more than four operators on a sequence, or a lambda nested two deep | |
| [Create, then return](rules/S3L0002.md) | S3L0002 | an object built across more than fifteen lines inside `return` or `yield return` | names the object |
| [Let it breathe](rules/S3L0003.md) | S3L0003 | no blank line before the final `return` of a block of four statements or more | adds the line |
| [No inline comments](rules/S3L0004.md) | S3L0004 | a comment inside a member body | |
| [English only](rules/S3L0005.md) | S3L0005 | Cyrillic in names, comments and the text of shipped code, except in a type or namespace named for prompts | |
| [No pattern names](rules/S3L0006.md) | S3L0006 | a type named `…Service`, `…Manager`, `…Helper`, `…Processor`, `…Data`, `…Util…`; a `Helpers`, `Utils`, `Managers`, `Common` or `Shared` namespace | |
| [No types in names](rules/S3L0007.md) | S3L0007 | `userList`, `userDict`, `nameString` | |
| [Booleans ask](rules/S3L0008.md) | S3L0008 | a boolean property or method that does not read as a question | |
| [No this qualifier](rules/S3L0009.md) | S3L0009 | `this.` where nothing is shadowed | removes it |
| [Keyword types](rules/S3L0010.md) | S3L0010 | `Int32`, `String` and the like where a keyword exists | writes the keyword |

The thresholds sit where the code of the Snail repositories is, as `tools/Metrics` measures it, so a rule flags the
outliers and not the habit. Two suppressors keep a release's promises: [S3LS0001](rules/S3LS0001.md) spares a shipped name
the naming rule (IDE1006), [S3LS0002](rules/S3LS0002.md) spares the code Razor generates RS0041. All of them:
[the rules](rules/index.md).

## Public API and versions

A library on NuGet has users its author cannot see. Its public API is written down in `PublicAPI.Shipped.txt` (what was
released) and `PublicAPI.Unshipped.txt` (what was added since), beside the project; a public symbol missing from them,
or listed and gone, fails the build. Package validation compares every pack with the release `SnailPackageBaseline`
names and fails on a breaking change. After a release the Unshipped lines move to Shipped and the baseline names the
new release ([releasing](releasing.md)).

A name already shipped is frozen: renaming a released member breaks its users, which is a decision for a major version,
not for a build. So the naming rules (S3L0006–S3L0008) judge only what is not in `PublicAPI.Shipped.txt`, and S3LS0001
spares a shipped type or member — the declaration itself, not a local or parameter of the same name — the `Async`
suffix and the other naming rules of IDE1006.

`Snail.Sdk.Mcp.Hosting`, `Snail.Sdk.Mcp.Http` and `Snail.Sdk.Mcp.Testing` keep their own API the same way, and their
reference is on this site: [Hosting](reference/Snail.Sdk.Mcp.Hosting.md), [Http](reference/Snail.Sdk.Mcp.Http.md),
[Testing](reference/Snail.Sdk.Mcp.Testing.md).

The version of a release comes from its tag (`v1.2.0`, `v1.2.0-preview.1`), which the shared release workflow passes
to the build as `-p:Version`: assemblies, packages and `.mcp/server.json` all carry it, and a server reports it in
`initialize`. Snail.Sdk's packages follow SemVer; the shared workflows are called by a major tag of their own
(`@v1`). What counts as breaking for each: [releasing](releasing.md).

## Tests

Every repository has a test project (xUnit, tests named `Subject_Condition_ExpectedResult`) and the shared CI runs it on
every push. Beyond what a test of the code itself asserts:

- **Guard tests** keep text and code in step, since a description is a contract a model obeys: every value of an enum
  named in the description that offers it, every limit stated in prose equal to the limit in code, every tool described.
- **A library** has a native probe, `tests/<Name>.NativeProbe`: a small app the CI publishes with Native AOT and runs.
  `IsAotCompatible` sees warnings only, and a library can be free of them and still bind nothing in a native app; the
  probe must exit 0. A library that cannot be native says why in `SnailNotNativeBecause` instead.
- **A server** tests itself as a program, through `Snail.Sdk.Mcp.Testing`:
    - **the stdio contract** — `ServerProgram.StartAsync(assembly, "SNAIL_<SHORT>_SERVER")` starts the server the way a
      client does, with a home folder of its own (`ServerProgram.Home`, a `TestFolder` deleted when the test process
      ends) and no settings file (`SNAIL_MCP_<SHORT>_CONFIG` names `ServerProgram.NoSettingsFile`, which never exists; a test that
      starts the process itself sets both), and calls its tools over stdio. It runs against the dll beside the tests, or against the program the variable names: the
      CI sets it to each native build, a developer to `~/.s3l-mcp/<name>/current`. Only the process shows what a
      native build gets wrong — a type it was not told about fails when the tool is listed or called, not when the
      program is built — and only the process shows a log line that strayed onto stdout. A server that starts and
      never answers `initialize` fails the test after two minutes with the program named, rather than holding the
      run until the CI's limit;
    - **the guard rules** — `ToolGuards.Problems(tools)` on the list a client receives: snake-case names listed once,
      the tool and every parameter described, read-only or destructive stated, every allowed value named as a word of
      its own, English;
    - **the HTTP contract** for a server with HTTP: the endpoint, its tools, the refusal of a browser's request.
- **Temporary folders a cleaner can prove are leftovers.** A test takes its temporary folder from
  `TestFolder.Create()` of `Snail.Sdk.Mcp.Testing`, never from `Directory.CreateTempSubdirectory()` or
  `Path.GetTempPath()` with a random name. The folder is made directly in the temporary folder and named
  `s3l-test-<pid>-<start>-<random>`: the id of the test process and the second it started, in Unix seconds (UTC), the
  same second the system reports as that process's start (`ps -o lstart`, `pbi_start_tvsec` of `proc_pidinfo` on
  macOS), then eight random lowercase letters and digits. It is deleted with everything in it when the test process
  exits; a run that crashes or is killed leaves it behind, and then the name is the proof: when no process with that
  id that started in that second is running, the folder is a leftover, and a cleaner (Snail.MCP.CleanUp) removes it
  without asking whose it is. A folder named at random proves nothing — a temporary folder holding thousands of
  them cannot be told apart from one still in use. `ServerProgram.Home` is such a folder. A test that deletes its
  folder sooner still may; a library's tests, which do not reference the package, name theirs by the same convention.
- **Mutation testing** (Stryker.NET, a pinned version) runs in `perf.yml` every two months and by hand: it says whether
  the tests catch a broken line, not only whether they run it. Its score is reported, and fails the run only under the
  caller's `mutation-break`.

## Performance

- **Benchmarks in every repository.** A `*.Benchmarks` project with BenchmarkDotNet and `[MemoryDiagnoser]` over the hot
  paths, and the CSV reports of an accepted run in `baseline/` beside it. `perf.yml` runs them every two months and by
  hand before a release that touches a hot path. Only a grown allocation fails it (by default past 5 %): allocations do
  not depend on the machine, while time on a shared runner moves by tens of percent between runs, so times are reported
  beside them. A new repository starts with the template's baseline and replaces it with the reports of its first
  `perf.yml` run (the run's `benchmarks` artifact), since the comparison is made on the workflow's runner.
- **Performance rules as errors where the gain is measured**: CA1869, CA1870, CA1851, CA1848, CA1852, SYSLIB1045
  ([Code style](#code-style)).
- **Native where the code allows it.** A server is native by default and optimised for size, and every library proves
  it works natively with its probe.

## Documentation and the site

One generator, `Snail.Sdk.Docs`, carried in the level packages and run by the build on request, writes the reference:

```bash
dotnet build -p:SnailReference=true
```

- For a **library** it writes `<assembly>.md` into `docs/reference/`, read from the built assembly through the references
  the compiler used, never run: the page shows what a user compiles against. Pages have no date and every list is
  sorted, so a page changes in a diff only when the API or its documentation did.
- For a **server** it writes `docs/reference/tools/index.md`, a page per tool family and `schemas/tools.json`: the
  server's own copy of the MCP SDK describes each tool, prompt and resource without invoking any, so the pages are what
  a client receives in `tools/list`. `docs/tools.json` names the families (one per class declaring tools), their slugs,
  titles and order, and the words of the index; it is validated before a page is written, and an error is one line
  (`error S3LD0001: …`, [the codes](conformance.md)).

The reference is committed, and the shared CI writes it again and fails when it differs, so the published pages are
never older than the code.

**The site** is the same for every library and server: the Snail theme and the MkDocs version it is built with, both
carried in the SDK, so a repository keeps no theme and no MkDocs of its own and a site changes look only with the SDK.
One target builds it, the same on a machine and in `docs.yml`:

```bash
dotnet msbuild src/Foo.csproj -t:SnailSite                         # obj/snail-site/site
dotnet msbuild src/Foo.csproj -t:SnailSite -p:SnailSiteServe=true  # served, reloading on a change
```

A repository with `docs/mkdocs.yml` keeps its navigation and pages and gets the theme over them. Any other gets a site
made from what it has: `README.md` as the home page (its first heading names the site), the pages of `docs/` in the order
its index links them, a section per folder, the reference last; links from the README into `docs/` stay links between
pages, links to other files of the repository go to GitHub. The build is strict: a broken link, a missing anchor or a page
the navigation leaves out fails it. MkDocs runs from a virtual environment the target makes in `obj/` on the first site of
each SDK version, so it needs Python 3 with its `venv` module and the package index once; `SnailSitePython` names an
interpreter that already has the pinned packages instead.

## Workflows

The CI and release of every repository are reusable workflows kept once, in Snail.Sdk's `.github/workflows/`, and a
composite action, `.github/actions/nuget-push`. A repository's own workflow files are short calls, `@v1`, so a fix
reaches every repository with the next move of the tag.

| Workflow | What it does |
| --- | --- |
| `lib-ci.yml` | tests, the conformance check, the reference compared with the committed one, the package packed and validated, every native probe published and run |
| `mcp-ci.yml` | tests, the conformance check, the reference; a native server built for each of its platforms on that platform, failing on any trim or AOT warning, and asked over stdio where it runs; a framework-dependent one published and asked the same way |
| `lib-publish.yml` | the version from the tag, the conformance check with the release checks, tests, the packages packed, validated and uploaded |
| `mcp-publish.yml` | the same for a server: a native package per platform asked over stdio there, the framework-dependent fallback, the package pointing at them, `.mcp/server.json` at the version |
| `nuget-push` (action) | pushes the uploaded packages to nuget.org through trusted publishing, the pointer last |
| `mcp-image.yml` | a container server's image, built on every push and pull request, pushed to GHCR from a version tag |
| `docs.yml` | the reference written again, then the site, to GitHub Pages |
| `perf.yml` | benchmarks against the baseline, and mutation testing |

The SDK comes from `global.json` (`global-json-file`), never from `dotnet-version: 10.x`, which would install a band
`rollForward: latestPatch` refuses. A workflow reads what it needs from the project (`AssemblyName`, `PackageId`,
`SnailMcpNative`, `ToolPackageRuntimeIdentifiers`) instead of repeating it in inputs. A release is two jobs of the
repository's own `publish.yml`: the shared workflow builds, tests and packs with no `id-token`, and a second job holds the
trusted publishing key and runs the `nuget-push` action — nuget.org gives that key only to a job of the publishing
repository's own workflow file, never to a job of Snail.Sdk's. Snail.Sdk's own CI runs actionlint and shellcheck on the
shared workflows, the action and the templates' workflows. Every job has a `timeout-minutes` — 30 for tests, docs and
packing, 60 for native builds, images and a release's push, 180 for benchmarks and mutation testing — so a step that
hangs (a native server deadlocked at start, a runner that lost its network) costs that, not the runner's six hours.

Every input, output, secret and permission, with the callers' files: [the workflows](workflows.md).

## Releasing

A release is a version tag, `vX.Y.Z`, on a green commit: `publish.yml` takes the version from it, checks that nothing
rests on a local package feed, tests, packs, and pushes through trusted publishing. Run by hand with *push* unticked, the
same workflow is a rehearsal that keeps every package as an artifact and pushes nothing. A container server's image is
pushed from the same tag. After a library's release, its `PublicAPI.Unshipped.txt` moves into `PublicAPI.Shipped.txt`
and `SnailPackageBaseline` names the release. The procedures, the first-time setup on GitHub and NuGet, the release of
Snail.Sdk itself, the `v1` tag and what counts as breaking: [releasing](releasing.md).

## Conformance

`Snail.Sdk.Conformance` reads a repository's files — nothing is built, run or changed — and answers check by check
(`S3LC0001`…). Snail.Sdk carries it, and the shared CI runs it on the repository itself:

```bash
dotnet msbuild src/Foo.csproj -t:SnailConformance                                  # on every push
dotnet msbuild src/Foo.csproj -t:SnailConformance -p:SnailConformanceRelease=true  # in a release
```

A required check failing is an MSBuild error carrying its id, so the build fails and CI annotates it; advice is a warning
and fails nothing. The checks compare a repository with the standard of the Snail.Sdk version it builds on: the
`global.json` sdk section, the `.editorconfig` and the shared workflows' inputs are embedded in the tool of that release.
Snail.Sdk's tests run it on projects made from the templates, which must conform. Every check, whom it asks and how to
fix it: [conformance](conformance.md).

## Upgrading Snail.Sdk and the .NET band

**Snail.Sdk.** A repository moves to a new release by changing the versions in `global.json`'s `msbuild-sdks` — one line
per level — then building, running the tests and the conformance check of the new release, and committing. The new
release may bring new warnings (a rule that became agreed) or a new required check — a breaking release, as
[releasing](releasing.md) defines it — listed on the [rules](rules/index.md) and [conformance](conformance.md) pages. A release that changes the shared `.editorconfig` or
the `global.json` sdk section needs the repository's copies replaced in the same commit, or S3LC0002 and S3LC0004 fail.

**The .NET SDK band.** The band is raised deliberately, in Snail.Sdk first, never by whoever installs a newer SDK: the
analyzers change with it. In Snail.Sdk: `global.json` `sdk.version`, the templates' `global.json` and `Dockerfile` build
stage, and the Roslyn packages the analyzers build against, then every suite run and a release. A repository then takes
that release and copies the new sdk section.

**The MCP SDK** (`ModelContextProtocol`) is a central version in each server's `Directory.Packages.props`. The server
libraries of a release are built against the version the template of that release names, and call its API, so a server
raises its own together with Snail.Sdk.

## Compatibility

What a release keeps so that servers and people that still use an earlier name keep working; each goes in the next major
version.

- **Folders under the snail names.** A server's folders are `~/.s3l-mcp-<short>` and `~/.config/s3l-mcp-<short>`;
  `ServerFolders` names the snail folder, `~/.snail-mcp-<short>` or `~/.config/snail-mcp-<short>`, while that real
  folder is the only one of the two. `ServerFolders.MoveFromSnailNames(name)`, called as an installed server starts,
  renames the snail folder to the s3l name and leaves a link at the old name, so a program still reading it finds the
  same files. It is hidden from IntelliSense.
- **`-p:Install=true`** is the same request as `-p:SnailMcpInstall=true`.
- **launchd jobs under the snail name.** When launchd still holds `com.snail.mcp.<short>` — the same server on the same
  port — an install prints the commands that retire it before those that load `s3l.mcp.<short>` ([installing](installing.md)).
- **`ServerProgram.Text(answer)`** is the same as `answer.Text()` of `ToolAnswers`, and hidden from IntelliSense.

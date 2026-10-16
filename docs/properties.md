# MSBuild properties

Every MSBuild property, item and target Snail.Sdk offers a project, per level, with its default and what it does; and
the properties the SDK sets on its own — which a repository may change and which it may not, and why. A name that
starts with `_` is the SDK's own and may change in any release; everything on this page is public and changes only
as the [versioning policy](releasing.md) says.

All public names start with `Snail`. MSBuild has one flat namespace of properties shared by Microsoft's SDKs, NuGet,
every package's targets and the project itself, so a prefix is the only way to keep a name from meaning something
else elsewhere; `Snail` is that prefix for every level and stays so.

## The levels

A project names one level in its `Sdk` attribute; each level includes the one before it.

| Level | For | Adds to the one before |
| --- | --- | --- |
| `Snail.Sdk` | any project | the rules of the standard, the analyzers, the project kinds, the conformance check |
| `Snail.Sdk.Library` | a library on NuGet | public API files, package validation, native compatibility, symbols, the API reference, the site |
| `Snail.Sdk.Mcp` | an MCP server and its tests | the server as a dotnet tool, Native AOT, transports, the tool reference, the install, the site |

```xml
<Project Sdk="Snail.Sdk.Library/0.1.0">
```

A project on another Microsoft SDK (Razor, Web) keeps it in the attribute and adds the level as an element,
`<Sdk Name="Snail.Sdk.Mcp" />`. The version usually lives once in `global.json` under `msbuild-sdks`.

## Snail.Sdk

### Properties a project or Directory.Build.props sets

| Property | Default | Effect |
| --- | --- | --- |
| `SnailProjectKind` | from the name: `Tests` for `*.Tests`, `Benchmarks` for `*.Benchmarks`, `NativeProbe` for `*.NativeProbe`; otherwise empty | The kind of project. `Tool` is set by hand: a program of the repository's own, such as a generator. See [the kinds](#project-kinds). |
| `SnailAnalyzers` | (true) | `false` leaves out the implicit `Snail.Sdk.Analyzers` reference. |
| `SnailEveryRuleAsAdvice` | (true) | `false` leaves out the generated config that lists every rule of the SDK's analysis level as a suggestion. |
| `SnailThreadingAnalyzersVersion` | `17.14.15` | The version of `Microsoft.VisualStudio.Threading.Analyzers` every project references. |
| `SnailConformanceRelease` | (false) | `true` with `-t:SnailConformance` adds the release checks (S3LC0026). The release workflows pass it. |

### Read, never set

| Property | Value |
| --- | --- |
| `SnailSdkVersion` | The release of Snail.Sdk the project builds with, written into the package. The levels reference `Snail.Sdk.Analyzers` and the `Snail.Sdk.Mcp.*` libraries of this same version. |

### Targets

| Target | Run | Does |
| --- | --- | --- |
| `SnailConformance` | `dotnet msbuild <a project> -t:SnailConformance` | Runs the [conformance check](conformance.md) on the repository the project is in (the folder of its `global.json`); an unmet required check is an MSBuild error carrying its id, advice a warning. Fails with a message when there is no `global.json` above the project. |
| `SnailAddEveryRuleAsAdvice` | before every compile | Reads the SDK's listing of every rule of its analysis level, makes each warning or error a suggestion and gives the compiler that config between the SDK's own (level −100) and `Snail.Sdk.globalconfig` (level 100). A newer SDK band brings its new rules as advice with no file edited. |

### Defaults and rules

The SDK sets two kinds of property. A **default** is set only when nothing has set it yet, so a repository's
`Directory.Build.props` (read before the SDK's props) or a project may change it. A **rule** of the standard is set
whatever `Directory.Build.props` says, because a whole repository must not opt out of the standard in one line; a
project body, which comes after, can still change it, and such a line is migration debt the
[conformance check](conformance.md) and a review see.

| Property | Value | Kind | Why |
| --- | --- | --- | --- |
| `LangVersion` | `latest` | default | the language the SDK band ships |
| `ImplicitUsings` | `enable` | default | |
| `Authors` | `orldev` | default | |
| `PackageLicenseExpression` | `MIT` | default | |
| `Nullable` | `enable` | rule | nullable references are part of every API; the reference prints the annotations |
| `GenerateDocumentationFile` | `true` | rule | XML docs on every public member (CS1591), and the reference is made from them |
| `TreatWarningsAsErrors` | `true` | rule | every agreed rule fails the build only through it |
| `EnforceCodeStyleInBuild` | `true` | rule | code style is checked in the build, not only in an editor |
| `AnalysisLevel` | `latest` | rule | the analyzers of the SDK band `global.json` pins |
| `AnalysisMode` | `Default` | rule | `Snail.Sdk.globalconfig` is calibrated to it: `Recommended` would give some 145 rules a warning each, and the first rule a newer SDK added would fail the build |
| `NuGetAudit`, `NuGetAuditMode`, `NuGetAuditLevel` | `true`, `all`, `low` | rule | a known vulnerability in any package, direct or transitive, fails the build |
| `Deterministic` | `true` | rule | the same sources give the same binaries |
| `ContinuousIntegrationBuild` | `true` on CI (`GITHUB_ACTIONS` or `CI` is `true`) | rule | a package maps the paths it was compiled from to the repository's, so no machine's folder is written into it |
| `PublishRepositoryUrl`, `EmbedUntrackedSources`, `RepositoryType` | `true`, `true`, `git` | rule | a package says which commit it was built from (SourceLink, with the .NET SDK) |

To live with one vulnerability advisory for a while, name it rather than lower the level for every package:
`<NuGetAuditSuppress Include="<advisory URL>" />`.

The SDK also adds, to every project: `Snail.Sdk.globalconfig` (the agreed rules as warnings), a reference to
`Snail.Sdk.Analyzers` of the same release (the [rules](rules/index.md)), and a reference to
`Microsoft.VisualStudio.Threading.Analyzers`. All are `PrivateAssets="all"`: nothing reaches a package's users.

### Project kinds

The kind decides what a project is spared and what it is. A kind is read from the name before any level's targets, so a
test project needs no lines of its own; a project whose name says otherwise sets `SnailProjectKind` in its body.

| Kind | Named | Output | Packed | Spared |
| --- | --- | --- | :-: | --- |
| (none) | anything else | as the project says | yes | nothing |
| `Tests` | `*.Tests` | as the test SDK says | no | CS1591, CA1707, the Async suffix, CA1001 (`Snail.Sdk.Tests.globalconfig`); its strings are data to S3L0005 |
| `Benchmarks` | `*.Benchmarks` | `Exe` | no | as `Tests` |
| `NativeProbe` | `*.NativeProbe` | `Exe`, `PublishAot`, `InvariantGlobalization` | no | CS1591, CA1707 |
| `Tool` | never: set `SnailProjectKind` | `Exe` | no | nothing — a tool is held to every rule of the code beside it |

A test project that sits directly in `tests/` leaves out `*.NativeProbe/**`, so a probe beside it is not compiled
into it. A probe on Microsoft.NET.Sdk turns off SignalR's custom awaitables, as the Web SDK does for a native app. The
Library and Mcp levels act only on a project of no kind: a test or benchmark gets none of a library's or server's lines.

## Snail.Sdk.Library

### Properties a project or Directory.Build.props sets

| Property | Default | Effect |
| --- | --- | --- |
| `SnailPackageBaseline` | (empty) | The last released version. Becomes `PackageValidationBaselineVersion`, so a breaking change to the package fails the build, and the `VersionPrefix` of a build that names no version, so a local pack is never below the baseline. Set in `Directory.Build.props` once released (S3LC0019). |
| `SnailNotNativeBecause` | (empty) | Why the library cannot work under Native AOT, in words. Set, `IsAotCompatible` is `false` and no `*.NativeProbe` is asked for (S3LC0020); empty, the library is native-compatible. |
| `SnailPublicApiAnalyzersVersion` | `5.6.0` | The version of `Microsoft.CodeAnalysis.PublicApiAnalyzers`. |
| `SnailReference` | (false) | `true` (`-p:SnailReference=true`) writes the API reference after the build. |
| `SnailReferenceFolder` | `docs/reference` beside `global.json` (the project folder's `docs/reference` without one) | The folder the reference pages are written into: `<AssemblyName>.md`. A trailing separator is accepted. |

### What it sets

| Property or item | Value | Why |
| --- | --- | --- |
| `EnablePackageValidation` | `true` | a package is checked against the last release before it can break its users |
| `IsAotCompatible` | `true`, unless `SnailNotNativeBecause` | the trim and AOT analyzers run on every library; beside the Razor or Web SDK they are turned on as Microsoft.NET.Sdk would |
| `IncludeSymbols`, `SymbolPackageFormat` | `true`, `snupkg` (default) | a package carries its symbols; not for a package with no assembly (`IncludeBuildOutput` false), which NuGet refuses (NU5017) |
| `Snail.Sdk.Library.globalconfig` | RS0016, RS0017, RS0022, RS0024–RS0027, RS0036, RS0037 as warnings | every public symbol is written down in `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`, so a change to the API shows in the diff |
| `Microsoft.CodeAnalysis.PublicApiAnalyzers` | `SnailPublicApiAnalyzersVersion` | reads those files |

### Targets

| Target | Run | Does |
| --- | --- | --- |
| `SnailReference` | after the build, with `-p:SnailReference=true` | Writes `<AssemblyName>.md` into `SnailReferenceFolder` from the assembly just built and its XML docs, read without being run through the references the compiler used. CI fails when the committed page differs. |
| `SnailSite` | `dotnet msbuild <project> -t:SnailSite` | Builds the site. See [the site](#the-site-both-levels). |

## Snail.Sdk.Mcp

### Properties a project or Directory.Build.props sets

| Property | Default | Effect |
| --- | --- | --- |
| `SnailMcpNative` | `true` | Published with Native AOT, a tool package per platform. `false` for code loaded at run time (Roslyn, MSBuild, Razor, Playwright). |
| `SnailMcpTransport` | `Stdio` | `Stdio`, `Http` or `Both`. `Http` and `Both` bring `Snail.Sdk.Mcp.Http` and a launchd job. |
| `SnailMcpContainer` | `false` | `true`: an image is built from the repository (S3LC0025); a publish with no runtime stays framework-dependent, as a Dockerfile needs. |
| `SnailMcpName` | `PackageId`, else `AssemblyName`, else the project name | The server's name: its folder under `SnailMcpRoot`. |
| `SnailMcpShortName` | `SnailMcpName` without a leading `Snail.MCP.` (any case), without dots, lower case: `Snail.MCP.Code.Context` is `codecontext` | Gives the command `s3l-mcp-<short>`, the launchd label `s3l.mcp.<short>`, the name a client sees in `initialize`. |
| `SnailReference` | (false) | `true` writes the tool reference after the build. |
| `SnailReferenceFolder` | `docs/reference` beside `global.json` | The folder the tool pages are written into: `tools/index.md`, a page per family, `schemas/tools.json`. A trailing separator is accepted. |
| `SnailReferenceSettings` | `docs/tools.json` beside `global.json` | The families of tools and the words of the index page. |
| `InvariantGlobalization` | `true` (default) | A server answers the same on every machine, and the native program carries no ICU. |
| `RollForward` | `LatestMajor` (default) | A framework-dependent server runs on any newer runtime installed. |
| `ServerGarbageCollection` | `false`, under the Web SDK too | The workstation GC: one client at a time needs none of the server GC's heaps per core. Measured on Snail.MCP.Roslyn with a solution loaded: 283 MB at idle, 481 MB with the server GC. The Web SDK's `true` is replaced, so a server that wants the server GC sets it in its project file. `ConcurrentGarbageCollection` stays the runtime's `true`: without it the same server sat at 355 MB. |
| `TieredPGO` | `false` when `SnailMcpNative` is `false` | A framework-dependent server runs through the JIT without profile-guided tiering. Measured: Snail.MCP.Roslyn 281 → 259 MB at rest and a quarter less CPU, Snail.MCP.CodeContext 793 → 759 MB and a faster index; the light servers unchanged and none slower. A native server has no JIT. A server that wants it sets `TieredPGO` in its project file. |

### Install properties

The install publishes a server into `~/.s3l-mcp/<SnailMcpName>/<date>-<time>.<commit>/` and points `current` at it
([Installing](installing.md)): `dotnet publish src/<Server> -c Release -r osx-arm64 -p:SnailMcpInstall=true`.

| Property or item | Default | Effect |
| --- | --- | --- |
| `SnailMcpInstall` | (false) | `true` installs on publish. `-p:Install=true` is the same request. |
| `SnailMcpRoot` | `$(HOME)/.s3l-mcp` | The folder of every installed server. |
| `SnailMcpKept` | `3` | How many publications are kept besides `current` and any a running process uses. |
| `SnailMcpLaunchAgent` | `deploy/s3l.mcp.<short>.plist` when it exists and the transport is not `Stdio` | The launchd job template, with `{{HOME}}`, `{{CURRENT}}` and `{{DOTNET}}` for this machine's paths; written beside the publications, never loaded by the build. |
| `SnailMcpWarmUp` | (empty) | Arguments of the warm-up run of a native program after the install, so macOS inspects a new binary now rather than when a session starts it. |
| item `SnailMcpCompanion` | (none) | Projects published into the same folder beside the server, such as a hook program. |

### What it sets

| Property or item | Value | Why |
| --- | --- | --- |
| `OutputType` | `Exe` | a server is a program |
| `PackAsTool`, `PackageType` | `true`, `McpServer` | the server is a dotnet tool of the MCP kind |
| `ToolCommandName` | `s3l-mcp-<short>` (default) | one command name rule for every server; also written into the assembly as `Snail.Sdk.Mcp.ServerName`, the name `ServerIdentity` reports in `initialize` |
| `IsAotCompatible` | `SnailMcpNative` (default) | |
| `CopyOutputSymbolsToPublishDirectory`, `PublishDocumentationFile`, `PublishReferencesDocumentationFiles` | `false` | a publication carries the program, not its symbols or API docs |
| `PublishAot` | `true` when native, unless a container build publishes with no runtime | |
| `OptimizationPreference` | `Size` (default, native) | the same speed, a smaller program |
| `EnableConfigurationBindingGenerator` | `true` (default, native) | settings are bound by generated code in every build alike |
| `EnableRequestDelegateGenerator` | `true` (default, native, not `Stdio`) | HTTP endpoints bound by generated code too |
| `ToolPackageRuntimeIdentifiers` | `osx-arm64;linux-x64;linux-arm64;win-x64;any` (default, native) | a tool package per platform and a framework-dependent one, `any`, for the rest; the workflows build exactly these |
| `PackageReadmeFile` | `README.md` when the repository has one | the README and `.mcp/server.json` are packed |
| package references | `Snail.Sdk.Mcp.Hosting` (server), `Snail.Sdk.Mcp.Http` (server, not `Stdio`), `Snail.Sdk.Mcp.Testing` (`Tests`), all of `SnailSdkVersion` | the libraries of the same release |

### Targets

| Target | Run | Does |
| --- | --- | --- |
| `SnailReference` | after the build, with `-p:SnailReference=true` | Writes the tool reference into `SnailReferenceFolder` from the server's own copy of the MCP SDK: the pages are what a client receives in `tools/list`; no tool runs. `docs/tools.json` is validated first (errors S3LD0001–S3LD0003, see [Conformance](conformance.md#the-reference-generator)). |
| `SnailMcpNamePublication` | before publish, on an install | Names the publication folder (time and commit, `-dirty` with uncommitted changes) and publishes into it; never over an existing one. |
| `SnailMcpInstall` | after publish, on an install | Publishes the companions, warms a native program up, points `current` at the publication, removes the old ones beyond `SnailMcpKept`, then writes the launchd job and prints or runs what loads it. |
| `SnailSite` | `dotnet msbuild <project> -t:SnailSite` | Builds the site. |

## The site (both levels)

`dotnet msbuild <the library or server project> -t:SnailSite` builds the site into `obj/snail-site/site`, strictly,
with the theme the SDK carries; `-p:SnailSiteServe=true` serves it, reloading on a change. A repository with
`docs/mkdocs.yml` keeps its navigation and pages and gets the theme; any other gets `README.md` as its home page, the
pages of `docs/` and the reference last. MkDocs runs from a virtual environment in `obj/snail-site/venv` with the
versions the SDK pins, made on the first build of each SDK version and again when its Python is gone.

| Property | Default | Effect |
| --- | --- | --- |
| `SnailSiteServe` | (false) | `true` serves the site instead of building it. |
| `SnailSiteFolder` | `obj/snail-site/site` | Where the site is built. |
| `SnailSiteName` | the README's first heading, else the repository's folder | The site's name, when there is no `docs/mkdocs.yml`. |
| `SnailSiteDescription` | `Description` | One sentence for every page. |
| `SnailSiteRepositoryUrl` | `RepositoryUrl` | The repository on GitHub: links from a page to a file outside `docs/` go there. |
| `SnailSiteLicense` | `PackageLicenseExpression` | The licence the footer names. |
| `SnailSitePython` | (empty) | An interpreter that already has the packages of the SDK's `requirements.txt`; empty, the build makes its own environment with `python3` (`python` on Windows). |

| Property | Read, never set |
| --- | --- |
| `SnailSiteTheme` | The theme folder, for a server whose own HTTP pages wear the theme (its `css/site.css`). |
| `SnailSiteProject` | `true` for a project of no kind: `docs.yml` builds the site on the first project under `src/` that says so. `SnailSite` on a project of any kind fails. |

The site needs Python 3 with its `venv` module (on Debian and Ubuntu, `python3-venv`) and the package index on the
first build of each SDK version. The errors say what to do: `SnailSitePython names <path>, which does not exist`,
`SnailSite makes MkDocs a virtual environment with python3, which could not make one`, and
`pip could not install the packages of <requirements.txt>` each name the fix — install Python with venv, reach the
index, or set `SnailSitePython`.

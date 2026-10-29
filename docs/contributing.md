# Contributing

Working on Snail.Sdk itself: how the repository is laid out, how it is built and tested, how a local version reaches the
repositories that build on it, and what a change to the public API, an analyzer rule, a conformance check or a template
takes. Snail.Sdk cannot build on the packages it makes, so it applies its own rules from source: `Directory.Build.props`
sets the same properties as `Snail.Sdk` and passes `src/Sdk/Sdk/Snail.Sdk.globalconfig` to every project.

## The repository

| Folder | Package | What it is |
| --- | --- | --- |
| `src/Sdk` | `Snail.Sdk` | the base MSBuild SDK: `Sdk/*.props`, `*.targets`, the global configs; carries the conformance tool in `tools/conformance` |
| `src/Library` | `Snail.Sdk.Library` | the library level; carries the docs tool and the site theme in `tools/docs` |
| `src/Mcp` | `Snail.Sdk.Mcp` | the server level, the install targets, the launchd script; carries the docs tool too |
| `src/Mcp.Hosting`, `src/Mcp.Http`, `src/Mcp.Testing`, `src/Mcp.JobClient` | the same names | the server libraries, and the client of their jobs |
| `src/Analyzers` | `Snail.Sdk.Analyzers` | the S3L rules, their code fixes and the suppressors |
| `src/Templates` | `Snail.Sdk.Templates` | `content/s3l-lib`, `content/s3l-mcp` |
| `src/Conformance` | (in `Snail.Sdk`) | `Snail.Sdk.Conformance`, the S3LC checks |
| `src/Docs` | (in the levels) | `Snail.Sdk.Docs`: the API and tool reference, the site configuration; `site/` holds the theme and the pinned MkDocs |
| `pack/` | | the README of each package, `pack/<PackageId>.md`, written for nuget.org with absolute links |
| `tests/` | | `Analyzers.Tests`, `Mcp.Tests`, `Sdk.Tests` |
| `tools/Metrics` | | measures the shapes the analyzer rules judge over folders of C#, so a threshold sits where the code is |
| `tools/Fleet` | | starts the stdio servers of the local Claude Code configuration as the client does and measures their start and memory |

Project folders are short; `Directory.Build.props` derives the package id, assembly name and root namespace from them
(`src/Mcp.Hosting` is `Snail.Sdk.Mcp.Hosting`). Every package has the version `VersionPrefix` of `Directory.Build.props`
unless a release passes `-p:Version`, and a packable project without its `pack/<PackageId>.md` fails to pack.

## Build and tests

```bash
dotnet build Snail.Sdk.slnx -c Release      # no warning: warnings are errors
dotnet test Snail.Sdk.slnx -c Release
```

| Project | Tests | Needs |
| --- | --- | --- |
| `Analyzers.Tests` | every rule and code fix on source snippets, the help links | nothing more |
| `Mcp.Tests` | the server libraries, including the HTTP host serving and refusing requests | nothing more |
| `Sdk.Tests` | the SDK as a build does it: every package packed once per run into a temporary feed (the next release with the run's moment, `<VersionPrefix>-test.<time>`, above the last release so package validation holds; what it restored is taken out of the package cache when the run ends), and real projects built on it — the levels, the kinds, the install and launchd targets (with a temporary home and a stand-in `launchctl`), the conformance tool, the docs tool, the site, the templates made, built, tested, packed and a server published natively and asked its contract | network access to nuget.org; `python3` with `venv` and access to PyPI (`SiteTests` make MkDocs an environment); the Native AOT toolchain of the machine |

The tests never write to the real home, never touch launchd and never pack into a shared feed. `dotnet pack
Snail.Sdk.slnx -m` is safe in parallel: the conformance and docs tools are published once, into their own `bin/`.

Snail.Sdk's `ci.yml` runs the tests, actionlint (with shellcheck) on `.github/workflows/*.yml` and the templates'
workflows, and shellcheck on the action's script. Before committing a workflow change, run them:

```bash
actionlint .github/workflows/*.yml src/Templates/content/*/.github/workflows/*.yml
shellcheck .github/actions/*/*.sh
```

The site of Snail.Sdk is built strictly by `tools/site.sh` (served with `tools/site.sh serve`), the same way its
`site.yml` builds it.

## A local version for the other repositories

Until a change is released, a repository that needs it takes Snail.Sdk from a local feed. Each pack takes a new number,
since NuGet never reads a version again once it is in the global packages folder:

```bash
dotnet pack Snail.Sdk.slnx -c Release -p:VersionSuffix=local.8 -o ~/nuget    # 0.3.1-local.8
dotnet nuget add source ~/nuget -n LocalPackages                             # once per machine
```

then, in the repository, `global.json`'s `msbuild-sdks` names `0.3.1-local.8`, and `nuget.config` maps the SDK to the
feed beside its other mappings — the longer pattern wins, so `Snail.Sdk.*` comes from the feed and everything else from
nuget.org:

```xml
<packageSource key="LocalPackages">
  <package pattern="Snail.Sdk" />
  <package pattern="Snail.Sdk.*" />
</packageSource>
```

The templates are installed again from `~/nuget/Snail.Sdk.Templates.0.3.1-local.8.nupkg`; their `global.json` is stamped
with the version being packed (`SNAIL_SDK_VERSION`), so they always name the SDK of their own pack. A repository on the
local feed builds, tests and passes the conformance check, but cannot be released: the release checks refuse a
`LocalPackages` mapping and a `-local` version (S3LC0026). Once the change is released, it moves back as
[releasing](releasing.md#from-the-local-feed-to-nuget) describes.

## The public API of the server libraries

`Snail.Sdk.Mcp.Hosting`, `.Http` and `.Testing` are built on plain `Microsoft.NET.Sdk` from source, and each references
`Microsoft.CodeAnalysis.PublicApiAnalyzers` itself. A public symbol added goes into the project's
`PublicAPI.Unshipped.txt` in the same commit — the build fails until it is there — and moves to
`PublicAPI.Shipped.txt` after the release that ships it. A symbol in `PublicAPI.Shipped.txt` is removed or changed only
in a breaking release ([releasing](releasing.md#what-counts-as-breaking)); until then a replaced member stays,
hidden with `[EditorBrowsable(EditorBrowsableState.Never)]`, forwarding to its replacement. Tests reach internals through
`InternalsVisibleTo` (`Snail.Sdk.Mcp.Tests`), so nothing is made public for a test.

## An analyzer rule

1. The descriptor in `src/Analyzers/Rules.cs`, with the next free `S3L` id, the category `Snail` and warning as its
   default severity; its help link is `https://orldev.github.io/Snail.Sdk/rules/<ID>/`, set by the same factory as the
   others.
2. The analyzer, with `EnableConcurrentExecution()` and generated code left out, asking the semantic model only when the
   syntax already looks like a finding: every Snail build runs it. A code fix where the change is mechanical; it writes
   the document's own line endings.
3. **Release tracking**: the rule goes into `src/Analyzers/AnalyzerReleases.Unshipped.md` under `### New Rules` (a
   severity or category change under `### Changed Rules`). The build fails (RS2000–RS2002) until it is there; at the
   release the entries move to `AnalyzerReleases.Shipped.md` under `## Release X.Y.Z`. A suppressor is not tracked by
   the analyzer, so it is written as a `;` comment line there.
4. Its severity in `src/Sdk/Sdk/Snail.Sdk.globalconfig`. A new rule at warning is a breaking change for every project.
5. Tests in `tests/Analyzers.Tests`: what it reports, what it leaves alone, the fix.
6. Its page, `docs/rules/<ID>.md`, in the site's navigation and in [the rules](rules/index.md).

A threshold is set by measuring: `dotnet run --project tools/Metrics -- <folders of C#>` prints the distribution of each
shape, and the rule flags the outliers, not the habit. The analyzers target `netstandard2.0` and the Roslyn of the
oldest compiler they must load in (`Microsoft.CodeAnalysis.CSharp.Workspaces` in `Directory.Packages.props`); Dependabot
leaves that version alone, and it is raised by hand with the SDK band, since a compiler refuses an analyzer built against
a newer Roslyn.

## A conformance check

1. A `Rule` in `src/Conformance/Rules.cs` with the next free id (`S3LC0033`, …), its name, `Required` or `Advice`, whom
   it asks (`all`, `library`, `server`, `native`), and what holds — the sentence `--rules` prints.
2. The check in `src/Conformance/Checks.cs`: it reads files only, never throws on a malformed file (a malformed file is a
   failed check with a detail that says what is wrong), and an unmet finding's detail says what to change.
3. Tests in `tests/Sdk.Tests/ConformanceTests.cs`; the templates must still conform (`TemplatesTests` run the check on
   every project they make).
4. The row in [conformance](conformance.md), copied from `dotnet run --project src/Conformance -- --rules`.

An id is never reused: a check that goes keeps its number retired. A new required check, or advice made required, is a
breaking change. The shared files a repository is compared with — `global.json`'s sdk section, the `.editorconfig`, the
shared workflows and the action — are embedded in the tool from this repository's own copies, so changing one of them
changes the standard of the next release.

## The templates

`src/Templates/content/s3l-lib` and `content/s3l-mcp` are complete repositories under their source names
(`Snail.Toolkit.Template`, `Snail.MCP.Template`, the short name `tmplshort`), which `dotnet new` replaces. Their options
and conditional files are in `.template.config/template.json`; `#if` blocks in C#, JSON, the Dockerfile and the plist
follow the options. They are the standard as files: a change to the standard is a change here, in the same commit.

- `TemplatesTests` make a library and servers of each shape from the packed templates and run what the shared CI runs:
  build, tests, conformance, the reference; the library packed with its symbols; the default server published natively
  for this machine and asked its contract as that program; the container shapes' Dockerfiles. Every build must end with
  `0 Warning(s)`.
- `TemplateWorkflowTests` check that every shared workflow or action a template's workflow calls exists, that every
  `with:` key is a declared input and every required input is passed, and that every output read is declared.
- The template's `Dockerfile` names the SDK band of `global.json`; a test holds them equal, so the band and the image
  move together.
- A template's benchmark baseline is a real report of its own benchmark, generated under the template's names so that
  `dotnet new` renames it with the project.
- Dependabot keeps the actions of the templates' workflows current, as it does for the shared workflows.

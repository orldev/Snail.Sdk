# Conformance

The conformance check reads a repository's files and says whether it is built the way the [standard](standard.md)
asks: the same `global.json`, `.editorconfig` and workflows as every other Snail repository, tests, benchmarks, a
written-down public API, a server's manifest. It runs in every CI and release, so a repository that drifts from the
standard fails its build the day it drifts rather than at its next release. This page lists every check with what it
requires and how to make it hold, how to run it, and the errors of the reference generator.

## Running it

In a repository on Snail.Sdk, on any of its projects:

```sh
dotnet msbuild src/<Project> -t:SnailConformance
dotnet msbuild src/<Project> -t:SnailConformance -p:SnailConformanceRelease=true   # with the release checks
```

The target runs the check on the folder of the project's `global.json`. Every unmet required check becomes an MSBuild
error carrying its id, `error S3LC0011: shared workflows: <what was seen>`, and every unmet advice a warning, so a build
log, an editor and CI show them like any other diagnostic. `lib-ci.yml` and `mcp-ci.yml` run it as it is;
`lib-publish.yml` and `mcp-publish.yml` run it with the release checks on.

The same check is a program, `Snail.Sdk.Conformance.dll` in the `tools/conformance` folder of the Snail.Sdk package
(from source: `dotnet run --project src/Conformance --`):

| Command | Does | Exit code |
| --- | --- | --- |
| `Snail.Sdk.Conformance <repository> [--release]` | checks one repository: every check, the unmet first, then each unmet one again as an MSBuild line | 0 when every required check holds, 1 when one fails, 2 on a wrong command line |
| `Snail.Sdk.Conformance --fleet <folder>... [--report <file.md>] [--release]` | checks every repository in the folders and writes a Markdown table, a column per check, then what each unmet check saw | always 0; stderr says how many conform |
| `Snail.Sdk.Conformance --rules` | prints the table of every check below | 0 |

`--release` adds the release checks (S3LC0026). A repository of a fleet is a folder holding `global.json` beside
`.git` — a folder, or a file as in a worktree or a submodule; Snail.Sdk itself is left out, being the standard.

A repository is a **server** when it keeps `.mcp/server.json` or its name starts with `Snail.MCP.`, and a **library**
otherwise. Ids are stable: a check that is dropped keeps its number retired, never reused.

## The checks

The table is the output of `--rules` of this release. "Fails the build" no means advice: reported, never failing.

| Id | Check | Asked of | Fails the build | What holds |
| --- | --- | --- | :-: | --- |
| S3LC0001 | one .slnx | all | yes | one `.slnx` at the root, no `.sln` |
| S3LC0002 | global.json | all | yes | the `sdk` section equal in value to Snail.Sdk's own (property order and formatting aside) |
| S3LC0003 | central versions | all | yes | `Directory.Packages.props` with `ManagePackageVersionsCentrally`, no `Version=` on a `PackageReference` in a project |
| S3LC0004 | .editorconfig | all | yes | the shared `.editorconfig`, line for line (CRLF or LF alike) |
| S3LC0005 | LICENSE | all | yes | a `LICENSE` file at the root |
| S3LC0006 | nuget.config | all | yes | `nuget.config` with a `packageSourceMapping` |
| S3LC0007 | on Snail.Sdk | all | yes | every project names a Snail.Sdk level, as its `Sdk` attribute or an `<Sdk>` element |
| S3LC0008 | tests | all | yes | a test project: `*.Tests`, or `SnailProjectKind` `Tests` |
| S3LC0009 | benchmarks | all | yes | a benchmark project: `*.Benchmarks`, or `SnailProjectKind` `Benchmarks` (a `Tool` does not count) |
| S3LC0010 | benchmark baseline | all | yes | `baseline/*-report.csv` beside a benchmark project — the reports perf.yml compares with |
| S3LC0011 | shared workflows | all | yes | `ci.yml`, `docs.yml`, `perf.yml` call the shared workflows of the right level; `publish.yml` has a job calling the shared `lib-publish.yml`/`mcp-publish.yml` without `id-token: write` and a job using the `nuget-push` action with it; a library may release with a workflow of its own (`nuget push`) in place of `publish.yml` |
| S3LC0012 | dependabot | all | yes | `.github/dependabot.yml` with `version: 2` and the `github-actions` and `nuget` ecosystems |
| S3LC0013 | README | all | yes | `README.md` whose first non-blank line is an H1 (`# `) |
| S3LC0014 | reference generated | all | yes | `docs/reference` committed, or a server's tool pages where `SnailReferenceFolder` writes them |
| S3LC0015 | site theme | all | yes | no `docs/theme`, `docs/requirements.txt` or `theme:` in `docs/mkdocs.yml` — the site wears the SDK's |
| S3LC0016 | native warnings | native | yes | a native repository (a library with a `*.NativeProbe`, a server with `SnailMcpNative` not false) silences no trim or AOT warning: no `IL` code in a `NoWarn`, and no `IlcTreatWarningsAsErrors` false unless a workflow of the repository names the forgiven warnings in `forgiven-native-warnings` (the shared CI then fails on any other) |
| S3LC0017 | migration debt | all | no | no `NoWarn` in any project or props file — advice |
| S3LC0018 | public API written down | library | yes | `PublicAPI.Shipped.txt` beside every packed project (not a kind, not `IsPackable` false) |
| S3LC0019 | package baseline | library | yes | `SnailPackageBaseline` in `Directory.Build.props` once released: a `PublicAPI.Shipped.txt` lists an API, or a `v*` tag of an earlier release exists — the tag of the release being made does not count |
| S3LC0020 | native probe | library | yes | a `*.NativeProbe` project, or `SnailNotNativeBecause` saying why not |
| S3LC0021 | server.json | server | yes | `.mcp/server.json` with the current registry `$schema`, a `name` ending in the short name, a `description` of at most 100 characters, a `version` other than `0.0.0-dev`, `packages` naming the repository's package, transports matching `SnailMcpTransport` (`Both`: `stdio` and `streamable-http`), every variable `SNAIL_MCP_<NAME>_` |
| S3LC0022 | tool families | server | yes | `docs/tools.json` |
| S3LC0023 | contract and guard rules | server | yes | a test through `Snail.Sdk.Mcp.Testing` and one with `ToolGuards` |
| S3LC0024 | data directory | server | no | its folders in the home under the s3l name, through `ServerFolders` — advice |
| S3LC0025 | container image | server | yes | with `SnailMcpContainer` true: a `Dockerfile`, and a workflow calling the shared `mcp-image.yml` |
| S3LC0026 | release feed | all, on release | yes | only when asked (`--release`, `-p:SnailConformanceRelease=true`): no `LocalPackages` source or mapping in `nuget.config`, no `-local` version in `global.json` `msbuild-sdks` or `Directory.Packages.props` |
| S3LC0027 | workflow inputs | all | yes | every call of a shared workflow or action (`orldev/Snail.Sdk/...`) names one Snail.Sdk keeps, passes only inputs it declares and every input it requires |
| S3LC0028 | release tag | all | no | `publish.yml` starts on `tags: ['v[0-9]+.[0-9]+.[0-9]+*']`, which the moving `v1` tag does not match — advice |
| S3LC0029 | job secrets | server with a launchd job | yes | the job template in `deploy/` names no variable that holds a secret (a `TOKEN`, `KEY`, `SECRET`, `PASSWORD` or `CREDENTIALS` in its name): a secret goes into the server's settings file |
| S3LC0030 | image version | server with an image | yes | every `Dockerfile` the shared `mcp-image.yml` builds takes the version it passes: each stage that runs `dotnet publish` declares `ARG VERSION`, with a default for a local build, and publishes with `-p:Version=$VERSION` |

## Making each check hold

The files the checks compare with — `global.json`, `.editorconfig`, the workflow callers, `dependabot.yml`,
`nuget.config` — are the ones `dotnet new s3l-lib` and `dotnet new s3l-mcp` write ([Getting started](getting-started.md));
copying them from a fresh repository of the template is the quickest fix for most checks.

| Id | Why | How to fix |
| --- | --- | --- |
| S3LC0001 | one solution format, the one the workflows find with `find -name '*.slnx'` | `dotnet sln migrate`, then delete the `.sln` |
| S3LC0002 | the code style analyzers ship with the .NET SDK: another band applies other rules to the same code | copy the `sdk` section of Snail.Sdk's `global.json` (order, spacing and line ends do not matter; `msbuild-sdks` and comments are the repository's own). The detail names each differing property |
| S3LC0003 | one version of each package for every project, raised in one place | `ManagePackageVersionsCentrally` in `Directory.Packages.props`, versions as `<PackageVersion>` there, none on a `<PackageReference>` |
| S3LC0004 | the rules ship in the SDK; a rule left in `.editorconfig` would outrank the package and freeze it | copy Snail.Sdk's `.editorconfig`; the detail names the first differing line |
| S3LC0005 | a package states its licence | add `LICENSE` |
| S3LC0006 | each package comes from the one source named for it | add `packageSourceMapping` to `nuget.config` |
| S3LC0007 | every rule of the standard comes through the SDK | put each project on a level: `Sdk="Snail.Sdk.Library"`, or `<Sdk Name="…" />` beside another Microsoft SDK |
| S3LC0008 | code without tests is not shipped | add a `*.Tests` project |
| S3LC0009 | performance is measured, not assumed | add a `*.Benchmarks` project with BenchmarkDotNet; a project of kind `Tool` does not count |
| S3LC0010 | perf.yml fails on a regression only against a baseline | copy the CSV reports of an accepted perf.yml run into `baseline/` beside the benchmark project (`benchmarks/baseline/` for a project directly in `benchmarks/`) |
| S3LC0011 | one CI for the fleet; a push only from a job nuget.org trusts | callers as in [Workflows](workflows.md): the release job without `id-token: write`, the push job with it and the `nuget-push` action |
| S3LC0012 | the SDK, the workflows' tag and the packages are kept current | `.github/dependabot.yml` with `version: 2` and the `github-actions` and `nuget` ecosystems, as the templates write it |
| S3LC0013 | the README is the site's home page and the package's page | open `README.md` with `# <Name>` |
| S3LC0014 | the reference on the site is the code's, and CI compares it | build with `-p:SnailReference=true` and commit `docs/reference` |
| S3LC0015 | every site looks the same and changes with the SDK | delete `docs/theme`, `docs/requirements.txt` and the `theme:` block of `docs/mkdocs.yml` |
| S3LC0016 | a silenced trim or AOT warning is a call that fails at run time in the native program | remove the `IL` codes from `NoWarn` and fix or annotate what they hid; a dependency's warnings the program never reaches are named in the CI's `forgiven-native-warnings` with `IlcTreatWarningsAsErrors` false; a server that cannot be native sets `SnailMcpNative` false |
| S3LC0017 | a `NoWarn` hides a rule from every new line too | fix what it hides, or suppress the one place with `[SuppressMessage]` and a reason; the detail lists each file and its codes |
| S3LC0018 | every public symbol is written down, so an API change shows in the diff | add `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` beside each packed project |
| S3LC0019 | a released package is validated against its last release | set `SnailPackageBaseline` to the last released version in `Directory.Build.props`. Released means a `PublicAPI.Shipped.txt` lists at least one API, or a `v*` tag of an earlier release exists. A tag on the commit checked out, or the one the workflow runs for, is the release being made: a first release passes without a baseline, which nuget.org could not give yet |
| S3LC0020 | `IsAotCompatible` sees warnings only; a probe proves the library binds in a native program | add `tests/<Name>.NativeProbe`, or set `SnailNotNativeBecause` to the reason |
| S3LC0021 | the registry and clients install the server from it | `$schema` `https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json`; `name` `<reverse DNS>/<…>-<short name>`; a real `version` (the release overwrites it); every `packages[].identifier` the repository's name; one package entry per transport — `stdio`, `streamable-http` with its `url`, both for `Both`; every environment variable `SNAIL_MCP_<SHORT>_…` |
| S3LC0022 | the tool reference is grouped by families | add `docs/tools.json`, validated by the [reference generator](#the-reference-generator) |
| S3LC0023 | the server is asked as a client asks it, and its tools pass the guard rules | a contract test through `ServerProgram` of [Snail.Sdk.Mcp.Testing](reference/Snail.Sdk.Mcp.Testing.md) and a test calling `ToolGuards.Problems` |
| S3LC0024 | one place for a server's data in every home | name the folders with `ServerFolders.DataDirectory`/`ConfigDirectory` |
| S3LC0025 | an image nobody builds breaks unseen | a `Dockerfile` at the root and an `image.yml` calling `mcp-image.yml` |
| S3LC0026 | a release must build where only nuget.org exists | remove the `LocalPackages` source and mapping from `nuget.config`, and every `-local` version from `global.json` and `Directory.Packages.props` |
| S3LC0027 | GitHub refuses a call with an unknown or missing input when it runs, in a repository that built green | pass only the inputs listed in [Workflows](workflows.md); the detail names the file, the line and the inputs |
| S3LC0028 | the moving `v1` tag must start no release | `tags: ['v[0-9]+.[0-9]+.[0-9]+*']` in `publish.yml` |
| S3LC0029 | a secret in the template travels with the repository, and a job edited by hand is no longer restarted by an install | move it into `~/.config/s3l-mcp-<short>/config.json`, which the server reads through `ServerSettings`, readable by its owner alone ([installing](installing.md#shared-servers-under-launchd)) |
| S3LC0030 | an image tagged `1.2.0` whose server says another version in `initialize` cannot be told from any other build | as the template writes it: `ARG VERSION=0.0.0-local` before the first `FROM`, `ARG VERSION` again in each stage that uses it, `-p:Version=$VERSION` on `dotnet publish`, and `LABEL org.opencontainers.image.version=$VERSION` in the final stage; the detail names the file and what is missing |

## The reference generator

`Snail.Sdk.Docs`, which the `SnailReference` and `SnailSite` targets run, stops on one line with an id, which the build
shows as an MSBuild error. Exit codes: 0 written, 1 an error line, 2 a wrong command line.

| Id | Means | Fix |
| --- | --- | --- |
| S3LD0001 | `docs/tools.json` is not valid: not JSON, an unknown key at the top or in a family, a family without a non-empty `type` or `title`, a `slug` outside `[a-z0-9-]+`, `index` as a slug, or a type or slug named twice | keep to the keys `intro` (paragraphs), `skillNote`, `openWorld`, `closedWorld` (text) and `families` (each `{ "type", "slug", "title" }`); `//` comments are allowed |
| S3LD0002 | `docs/tools.json` and the server disagree: a class declaring tools has no family, a family names a class that declares none, or two families land on one page | one family per class that declares tools, and only those |
| S3LD0003 | an input cannot be read or an output written | the message names the file |

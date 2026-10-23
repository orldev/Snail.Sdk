# Getting started

How to start a new Snail library, MCP server or ASP.NET application from the templates, how to bring an existing
repository onto Snail.Sdk, what to do on a project's first day, and how to run locally everything the shared CI runs. A
project made from a template builds without a warning, passes its tests and the conformance check, and — a server —
answers its contract as a native program, before a line of it is changed.

## Prerequisites

- The .NET SDK of the band in the template's `global.json` (10.0.3xx): `rollForward: latestPatch` refuses any other band,
  because the analyzers that judge the code ship with the SDK.
- For a native server or a library's native probe: the platform's Native AOT toolchain (Xcode's command line tools on
  macOS, `clang` and `zlib1g-dev` on Linux).
- For the site: Python 3 with its `venv` module (on Debian and Ubuntu, `python3-venv`).

## A new project

```bash
dotnet new install Snail.Sdk.Templates
dotnet new s3l-lib -n Snail.Toolkit.Foo
dotnet new s3l-mcp -n Snail.MCP.Foo
dotnet new s3l-web -n Vertical.Foo
```

The templates build on the Snail.Sdk of their own release: `global.json` names it in `msbuild-sdks`. The project's name
is the repository's and the package's; for a server, the [short name](standard.md#names) (`foo`) names its tools, its
command `s3l-mcp-foo`, its variables `SNAIL_MCP_FOO_*`, the contract variable `SNAIL_FOO_SERVER` and its launchd job.

| Template | Option | Default | Meaning |
| --- | --- | --- | --- |
| all | `--description` | a placeholder sentence | the package description, the README's first paragraph, a server's instructions |
| `s3l-mcp` | `--native` | `true` | published with Native AOT (`SnailMcpNative`); `false` for a server that loads code at run time |
| `s3l-mcp` | `--transport` | `Stdio` | `Stdio`: a process per session; `Http`: one server for every session of the machine, over loopback HTTP, with a launchd job in `deploy/`; `Both`: stdio, or HTTP when `SNAIL_MCP_<SHORT>_TRANSPORT=http` |
| `s3l-mcp` | `--container` | `false` | `true` adds a `Dockerfile` (native or framework-dependent as `--native` says), `.dockerignore` and `image.yml` |
| `s3l-mcp` | `--port` | `9300` | the loopback port of the HTTP endpoint |
| `s3l-web` | `--kind` | `api` | `api`: a minimal API; `blazor`: Blazor Server pages styled with Tailwind |
| `s3l-web` | `--auth` | `none` | `cookie`: a sign-in cookie, its keys on a volume; `jwt`: a bearer token through Snail.Toolkit.Authentication.JwtBearer |
| `s3l-web` | `--mongo`, `--rabbit`, `--vault` | `false` | MongoDB, RabbitMQ, and configuration from HashiCorp Vault, through their Snail.Toolkit packages |
| `s3l-web` | `--deploy` | `compose` | `compose`: docker compose behind traefik; `nomad`: a Nomad job behind traefik |
| `s3l-web` | `--port` | `5080` | the local port in development |

```bash
dotnet new s3l-mcp -n Snail.MCP.Foo --native false --transport Both --port 9310 --description "Lets an agent …"
```

What each template writes: [the repository layout](standard.md#repository-layout).

## Templates of a given release

`dotnet new install Snail.Sdk.Templates` takes the latest release; `dotnet new install Snail.Sdk.Templates::0.2.0` a
given one. The template's `nuget.config` maps every package to nuget.org, and nothing else is needed. A version of
Snail.Sdk not yet released comes from a local feed: [contributing](contributing.md#a-local-version-for-the-other-repositories).

## An existing repository

A repository written before Snail.Sdk moves onto it in one change: the files a template writes, the SDK in each project,
and then whatever the analyzers and the conformance check find in the code. Make a project of the same kind from the
template beside it first, `dotnet new s3l-web -n Vertical.Foo -o ~/scratch/Vertical.Foo` — its files are the ones to
copy, and the steps below name them.

1. **The root files.** Copy `global.json` (its `msbuild-sdks` names the Snail.Sdk release), `.editorconfig` as it is —
   the rules ship in the SDK, and a rule kept here would outrank it — and `LICENSE`. In `nuget.config`, keep the
   repository's own sources and add a `packageSourceMapping` for them. Copy `.github/dependabot.yml` and the workflows
   that call the shared ones: `ci.yml`, `perf.yml`, and `publish.yml` for a package or `image.yml` for an image.
2. **`Directory.Build.props`.** Take out what the SDK sets — `LangVersion`, `Nullable`, `ImplicitUsings`,
   `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `GenerateDocumentationFile`, `AnalysisLevel`, any `NoWarn`. What
   stays is the repository's own: `Product`, the target framework, central package management, a version prefix. The
   conformance check names a line left behind (S3LC0017).
3. **The projects.** A library builds on `Sdk="Snail.Sdk.Library"`, a server on `Sdk="Snail.Sdk.Mcp"`, an application
   keeps `Sdk="Microsoft.NET.Sdk.Web"` and adds `<Sdk Name="Snail.Sdk" />` inside the project. A test project builds on
   `Sdk="Snail.Sdk"`, and loses its `<GenerateDocumentationFile>false</GenerateDocumentationFile>`: the SDK already asks
   no documentation of tests, and with the file switched off the build refuses to run IDE0005 and fails.
4. **The build.** `dotnet build -c Release --no-incremental` — an incremental build may skip the compiler, and with it
   the new rules. What it reports is the code's own debt; [the rules](rules/index.md) say how each is fixed. Two are
   common in code written before: `using` directives no longer needed (IDE0005, found only once documentation files are
   on), and text in another language than English (S3L0005) — a prompt moves into a type named for it, and what a person
   reads into resources ([the standard](standard.md#repository-layout)).
5. **What a kind asks.** A library: its public API in `PublicAPI.Shipped.txt` as last released and
   `SnailPackageBaseline` naming that release, or both empty before the first one. A server: `.mcp/server.json` and
   `docs/tools.json`. An image: `ARG VERSION` with `-p:Version=$VERSION` on `dotnet publish` (S3LC0030), and tini as
   process 1, as the template's `Dockerfile` writes them.
6. **Benchmarks.** A benchmark project in `benchmarks/` with its first reports in `baseline/` (S3LC0009, S3LC0010).
7. **The check.** `dotnet test -c Release`, then `dotnet msbuild src/<Project>.csproj -t:SnailConformance` until every
   line holds, then one commit.

## The first day

1. **Git**: `git init -b master`, a first commit of everything the template wrote, and the repository on GitHub under
   the project's name (`orldev/Snail.MCP.Foo`); the template's `RepositoryUrl` already names it.
2. **Your code**: replace the sample (`Greeting` of a library, the `foo_diagnose` tool and its family in
   `docs/tools.json` of a server) with the first real type or tool.
3. **The public API** of a library: build, and add each public symbol the build reports (RS0016) to
   `src/PublicAPI.Unshipped.txt`. It moves to `PublicAPI.Shipped.txt` at the first release.
4. **The reference**: `dotnet build -c Release -p:SnailReference=true`, and commit `docs/reference`. The CI writes it
   again and fails when it differs.
5. **The benchmark baseline**: the template's `baseline/` is a placeholder measured elsewhere; replace it with the
   reports of the first `perf.yml` run (below).
6. **GitHub settings**: Pages source *GitHub Actions*; default branch `master`; for a container server, the image made
   public after its first push. Details: [first-time setup](releasing.md#first-time-setup).
7. **NuGet**: the secret `NUGET_USER` = `devorl`, and a trusted publishing policy for the repository naming
   `publish.yml`. Without them the release builds and checks everything and its push job fails.

## Running everything locally

What the shared CI runs, on a developer's machine:

| What | Command |
| --- | --- |
| build and tests | `dotnet test -c Release` |
| conformance | `dotnet msbuild src/<Project>.csproj -t:SnailConformance` |
| conformance, as a release checks it | `dotnet msbuild src/<Project>.csproj -t:SnailConformance -p:SnailConformanceRelease=true` |
| the reference | `dotnet build -c Release -p:SnailReference=true`, then `git status docs` |
| the site | `dotnet msbuild src/<Project>.csproj -t:SnailSite` (into `obj/snail-site/site`) |
| the site, served and reloading | `dotnet msbuild src/<Project>.csproj -t:SnailSite -p:SnailSiteServe=true` |
| a library's native probe | `dotnet publish tests/<Name>.NativeProbe -c Release -r osx-arm64 -o probe`, then `probe/<Name>.NativeProbe` |
| a server's contract against its native build | `dotnet publish src -c Release -r osx-arm64 -o out`, then `SNAIL_<SHORT>_SERVER=$PWD/out/<Name> dotnet test tests --filter "FullyQualifiedName~Contract"` |
| benchmarks | `dotnet run -c Release --project benchmarks/<Benchmarks project> -- --filter '*' --job short --exporters csv` |
| install a server | `dotnet publish src -c Release -r osx-arm64 -p:SnailMcpInstall=true` ([installing](installing.md)) |

**The benchmark baseline** is the CSV reports BenchmarkDotNet writes, `*-report.csv`, copied into `baseline/` beside the
benchmark project when a change is accepted. `perf.yml` compares on its own runner (`ubuntu-latest` unless its `os`
input says otherwise), so the baseline comes from a run of `perf.yml`: download the run's `benchmarks` artifact and copy
its `*-report.csv` files. A local run shows the allocations, which do not depend on the machine, and the times, which do.

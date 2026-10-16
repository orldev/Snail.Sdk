# Getting started

How to start a new Snail library or MCP server from the templates, with Snail.Sdk from NuGet or from a local feed, what
to do on its first day, and how to run locally everything the shared CI runs. A project made from a template builds
without a warning, passes its tests and the conformance check, and — a server — answers its contract as a native
program, before a line of it is changed.

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
```

The templates build on the Snail.Sdk of their own release: `global.json` names it in `msbuild-sdks`. The project's name
is the repository's and the package's; for a server, the [short name](standard.md#names) (`foo`) names its tools, its
command `s3l-mcp-foo`, its variables `SNAIL_MCP_FOO_*`, the contract variable `SNAIL_FOO_SERVER` and its launchd job.

| Template | Option | Default | Meaning |
| --- | --- | --- | --- |
| both | `--description` | a placeholder sentence | the package description, the README's first paragraph, a server's instructions |
| `s3l-mcp` | `--native` | `true` | published with Native AOT (`SnailMcpNative`); `false` for a server that loads code at run time |
| `s3l-mcp` | `--transport` | `Stdio` | `Stdio`: a process per session; `Http`: one server for every session of the machine, over loopback HTTP, with a launchd job in `deploy/`; `Both`: stdio, or HTTP when `SNAIL_MCP_<SHORT>_TRANSPORT=http` |
| `s3l-mcp` | `--container` | `false` | `true` adds a `Dockerfile` (native or framework-dependent as `--native` says), `.dockerignore` and `image.yml` |
| `s3l-mcp` | `--port` | `9300` | the loopback port of the HTTP endpoint |

```bash
dotnet new s3l-mcp -n Snail.MCP.Foo --native false --transport Both --port 9310 --description "Lets an agent …"
```

What each template writes: [the repository layout](standard.md#repository-layout).

## Before Snail.Sdk is on NuGet

Until a release of Snail.Sdk is on nuget.org, its packages come from a local folder feed. Each repository then names a
local version of the SDK, `0.1.0-local.N`.

1. **Pack Snail.Sdk into the feed**, with a version not packed before — NuGet never reads a version again once it is in
   the global packages folder, so a repacked `0.1.0-local.7` would be ignored:

   ```bash
   cd Snail.Sdk
   dotnet pack Snail.Sdk.slnx -c Release -p:VersionSuffix=local.8 -o ~/nuget     # 0.1.0-local.8
   ```

2. **Name the feed once per machine**, in the user-level NuGet configuration, under the key the repositories map to:

   ```bash
   dotnet nuget add source ~/nuget -n LocalPackages
   ```

3. **Install the templates from the feed**:

   ```bash
   dotnet new install ~/nuget/Snail.Sdk.Templates.0.1.0-local.8.nupkg
   ```

   The new project's `global.json` names `0.1.0-local.8` for every level.

4. **Map the Snail packages to the feed** in the new repository's `nuget.config`, beside the template's mapping:

   ```xml
   <packageSourceMapping>
     <packageSource key="nuget.org">
       <package pattern="*" />
     </packageSource>
     <packageSource key="LocalPackages">
       <package pattern="Snail.Sdk" />
       <package pattern="Snail.Sdk.*" />
     </packageSource>
   </packageSourceMapping>
   ```

   The longer pattern wins, so `Snail.Sdk.*` comes from the feed and everything else from nuget.org.

A repository on the local feed builds, tests and passes the conformance check as any other, but it cannot be released:
the release checks refuse a `LocalPackages` mapping and a `-local` version. When Snail.Sdk is released, the repository
switches as [releasing](releasing.md#from-the-local-feed-to-nuget) describes.

## After Snail.Sdk is on NuGet

`dotnet new install Snail.Sdk.Templates` takes the latest release; `dotnet new install Snail.Sdk.Templates::0.2.0` a
given one. The template's `nuget.config` maps every package to nuget.org, and nothing else is needed.

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

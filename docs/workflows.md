# Workflows

Every Snail library and server runs its CI, its release, its site and its benchmarks through the shared workflows of
Snail.Sdk, so that a fix to how the fleet builds is made once, here, and every repository gets it on its next run. A
repository keeps only short callers in `.github/workflows/`; this page lists each shared workflow and the `nuget-push`
action with a caller, its inputs, outputs, secrets and permissions. The input tables are taken from the workflow files
as they are in this release.

## Calling them: `@v1`

A caller names a workflow by the tag `v1`:

```yaml
uses: orldev/Snail.Sdk/.github/workflows/lib-ci.yml@v1
```

`v1` is the major version of the workflow and action contract, not the version of a package. It is a moving tag: it
points at the commit of a package release `vX.Y.Z` whose workflows and action keep every `v1` caller working, and the
owner moves it after that release is on nuget.org. A change that would break a `v1` caller — a removed or renamed
input, output, secret or artifact, an input made required, a default that makes a run do something else, a permission
the caller must newly grant — is released under a new tag, `v2`, and `v1` stays where it was. Callers therefore get
fixes without editing anything, and never a break they did not choose. Dependabot keeps the callers' tag current: it
proposes `@v2` when one exists.

| Workflow | Called from | Does |
| --- | --- | --- |
| [`lib-ci.yml`](#lib-ciyml) | a library's `ci.yml` | tests, conformance, the reference, the package, the native probe |
| [`lib-publish.yml`](#lib-publishyml) | a library's `publish.yml` | the release: tests, pack, artifact `packages` |
| [`mcp-ci.yml`](#mcp-ciyml) | a server's `ci.yml` | tests, conformance, the reference, the native build per platform, the stdio contract |
| [`mcp-publish.yml`](#mcp-publishyml) | a server's `publish.yml` | the release: a tool package per platform, the fallback and the pointer |
| [`mcp-image.yml`](#mcp-imageyml) | a container server's `image.yml` | the image, pushed to GHCR from a version tag |
| [`docs.yml`](#docsyml) | every repository's `docs.yml` | the site on GitHub Pages |
| [`perf.yml`](#perfyml) | every repository's `perf.yml` | benchmarks against the baseline, mutation testing |
| [`nuget-push`](#the-nuget-push-action) (action) | the push job of a `publish.yml` | pushes the packages through NuGet trusted publishing |

Every shared workflow declares `permissions: contents: read` and asks for nothing more except where the tables below
say so. The [conformance check](conformance.md) holds each caller to this page: S3LC0011 checks that the four callers
exist and call the workflows of the repository's level, S3LC0027 that every call names a workflow or action Snail.Sdk
keeps, passes only inputs it declares and every input it requires.

## lib-ci.yml

The build and tests of a library, on every push and pull request. The job `test` runs `dotnet test` on the solution,
then the [conformance check](conformance.md) (`-t:SnailConformance` on the first project under `src/`), then builds
with `-p:SnailReference=true` and fails when `docs/` differs from what is committed — the reference on the site is
always the code's — and packs, so package validation against `SnailPackageBaseline` runs on every change. The job
`native-probe` publishes every `*.NativeProbe` project with Native AOT on Linux, fails on any `IL` or `AOT` warning,
and runs the program: a library that only claims to be native is caught here.

```yaml
name: build and tests
on:
  push:
  pull_request:
  workflow_dispatch:
permissions:
  contents: read
jobs:
  ci:
    uses: orldev/Snail.Sdk/.github/workflows/lib-ci.yml@v1
```

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `solution` | string | `''` | The solution to build; the one .slnx at the root when empty. |
| `os` | string | `ubuntu-latest` | The runner the tests run on. |
| `test-filter` | string | `''` | A dotnet test filter, such as one that leaves out tests needing a container. |
| `test-environment` | string | `''` | Variables the tests run with, one NAME=value a line, such as one that turns a skipped container test into a failure. |
| `forgiven-native-warnings` | string | `''` | An extended regular expression of native warnings a probe may hear — a dependency's, explained in the probe project, such as 'Microsoft\.AspNetCore\.Components'. Every other trim or AOT warning still fails. |

Secrets: `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN`, both optional, passed with `secrets: inherit`. They exist only for
tests that pull an image Docker Hub gives to a signed-in pull; without them the login step is skipped. Docker Hub is
used for pulling test images only — Snail images are published to GHCR.

Artifacts: `test results` (the `.trx` files).

## lib-publish.yml

The release of a library, on a version tag or by hand. It checks the version (`vX.Y.Z` or `vX.Y.Z-suffix`), runs the
conformance check with the release checks on (`-p:SnailConformanceRelease=true`: nothing may rest on a local feed),
tests, packs every package with `-p:Version=<version>` and uploads them as the artifact `packages`. It pushes nothing
and holds no `id-token`: the push is a job of the caller's own, [for the reason below](#why-the-push-is-the-callers-job).

```yaml
name: release to NuGet
on:
  push:
    tags: ['v[0-9]+.[0-9]+.[0-9]+*']
  workflow_dispatch:
    inputs:
      version:
        description: The version to pack, such as 0.1.0 or 0.1.0-preview.1
        required: true
      push:
        description: Push the packages to NuGet
        type: boolean
        default: false
permissions:
  contents: read
jobs:
  release:
    uses: orldev/Snail.Sdk/.github/workflows/lib-publish.yml@v1
    with:
      version: ${{ inputs.version }}
      push: ${{ github.event_name == 'push' || inputs.push }}
    secrets: inherit
  push:
    needs: release
    if: needs.release.outputs.push == 'true'
    runs-on: ubuntu-latest
    permissions:
      id-token: write
      contents: read
    steps:
      - uses: orldev/Snail.Sdk/.github/actions/nuget-push@v1
        with:
          user: ${{ secrets.NUGET_USER }}
```

The tag pattern `v[0-9]+.[0-9]+.[0-9]+*` starts a release on `v1.2.0` and `v1.2.0-preview.1` but never on a moving
major tag such as `v1` (S3LC0028 advises it).

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `version` | string | `''` | The version to pack; the tag without its v when empty. |
| `push` | boolean | `false` | Whether the caller pushes the packages to NuGet; passed through to the output push. |
| `os` | string | `ubuntu-latest` | The runner the tests run on and the packages are packed on. |
| `test-filter` | string | `''` | A dotnet test filter, such as one that leaves out tests needing a container. |
| `test-environment` | string | `''` | Variables the tests run with, one NAME=value a line, such as one that turns a skipped container test into a failure. |

| Output | Meaning |
| --- | --- |
| `version` | The version the packages carry. |
| `push` | `'true'` when the packages are to be pushed: the condition of the caller's push job. |

Secrets: `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, optional, as in `lib-ci.yml`. Artifact: `packages` (`*.nupkg`,
`*.snupkg`), kept 7 days.

## mcp-ci.yml

The build and tests of an MCP server. The job `test` runs the tests, the conformance check and the reference check as
for a library. The job `project` reads from the project, with one `dotnet msbuild -getProperty`, the program's name
(`AssemblyName`), whether the server is native (`SnailMcpNative`) and its platforms (`ToolPackageRuntimeIdentifiers`),
so the caller never repeats what the project says. A native server is then published with Native AOT on a runner of
each platform — any trim or AOT warning fails it, since such a warning is a call that fails at run time — and, where
the runner can run it, its contract tests run against that program through `contract-variable`. A framework-dependent
server is published once and asked the same way.

```yaml
jobs:
  ci:
    uses: orldev/Snail.Sdk/.github/workflows/mcp-ci.yml@v1
    with:
      contract-variable: SNAIL_CLEANUP_SERVER
```

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `project` | string | `src` | The server's project, a folder or a .csproj. |
| `tests` | string | `tests` | The test project, a folder or a .csproj. |
| `os` | string | `ubuntu-latest` | The runner the tests run on, and a framework-dependent server is asked on; macos-latest for a server that reads the Mac. |
| `test-filter` | string | `''` | A dotnet test filter for the tests, such as one leaving out those that read the real machine. |
| `runtimes` | string | `''` | The platforms a native server ships to, as a JSON list of { rid, os, runs }; empty takes them from the project's ToolPackageRuntimeIdentifiers, so the workflow builds exactly the packages the pointer names. |
| `forgiven-native-warnings` | string | `''` | An extended regular expression of native warnings that do not fail the build — a dependency's, explained in the project, such as 'Jint\.dll\|: Jint\.'. Every other trim or AOT warning still does. |
| `contract-variable` | string | required | The variable the stdio contract tests read the program from, such as SNAIL_CLEANUP_SERVER. |
| `contract-filter` | string | `FullyQualifiedName~Contract` | The dotnet test filter that selects the contract tests. |

The contract variable is `SNAIL_<SHORT>_SERVER`, `<SHORT>` being the server's short name in capitals; the tests read it
through `ServerProgram.Command` of [Snail.Sdk.Mcp.Testing](reference/Snail.Sdk.Mcp.Testing.md).

Each platform runs on its own runner:

| Platform | Runner | Contract run |
| --- | --- | :-: |
| `osx-arm64` | `macos-latest` | yes |
| `osx-x64` | `macos-latest` | no — built only, the runner is ARM |
| `linux-x64` | `ubuntu-latest` | yes |
| `linux-arm64` | `ubuntu-24.04-arm` | yes |
| `win-x64` | `windows-latest` | yes |
| `win-arm64` | `windows-11-arm` | yes |

`any` (the framework-dependent fallback) is not built per platform; a platform outside the table fails the job, so a
new one is a change to this workflow, not a silent gap. Artifacts: `test results`.

## mcp-publish.yml

The release of a server as a dotnet tool. Inputs are those of `mcp-ci.yml`, so the caller passes the same values in
both files, plus `also-pack`, `version` and `push`. The job `version` checks the version; `test` runs the conformance
check with the release checks on and the tests; `project` reads the project as in CI, and also its `PackageId`. A
native server is packed on each platform's runner (`dotnet pack -r <rid>`), published and asked over stdio there, and
uploaded as `packages-<rid>`; the job `portable` packs the framework-dependent fallback (`-r any`), the pointer package
that names the per-platform packages, and every `also-pack` project, and uploads them as `packages-portable`. Every
package and its assemblies carry `-p:Version=<version>`, so the version a server reports is the one released, and
`.mcp/server.json` is rewritten with it before packing. The job fails when the server's package was not packed or,
for a native server, does not name `RuntimeIdentifierPackages`.

```yaml
jobs:
  release:
    uses: orldev/Snail.Sdk/.github/workflows/mcp-publish.yml@v1
    with:
      contract-variable: SNAIL_CLEANUP_SERVER
      version: ${{ inputs.version }}
      push: ${{ github.event_name == 'push' || inputs.push }}
  push:
    needs: release
    if: needs.release.outputs.push == 'true'
    runs-on: ubuntu-latest
    permissions:
      id-token: write
      contents: read
    steps:
      - uses: orldev/Snail.Sdk/.github/actions/nuget-push@v1
        with:
          user: ${{ secrets.NUGET_USER }}
```

The `on:` block is the library's above. `mcp-publish.yml` takes no secrets, so the release job needs no
`secrets: inherit`.

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `project` | string | `src` | The server's project, a folder or a .csproj. |
| `tests` | string | `tests` | The test project, a folder or a .csproj. |
| `os` | string | `ubuntu-latest` | The runner the tests run on. |
| `test-filter` | string | `''` | A dotnet test filter for the tests, such as one leaving out those that read the real machine. |
| `runtimes` | string | `''` | The platforms a native server ships to, as a JSON list of { rid, os, runs }; empty takes them from the project's ToolPackageRuntimeIdentifiers, so the workflow builds exactly the packages the pointer names. |
| `forgiven-native-warnings` | string | `''` | An extended regular expression of native warnings that do not fail the build — a dependency's, explained in the project, such as 'Jint\.dll\|: Jint\.'. Every other trim or AOT warning still does. |
| `contract-variable` | string | required | The variable the stdio contract tests read the program from. |
| `contract-filter` | string | `FullyQualifiedName~Contract` | The dotnet test filter that selects the contract tests. |
| `also-pack` | string | `''` | Other projects released with the server, separated by spaces, such as src/Core. |
| `version` | string | `''` | The version to pack; the tag without its v when empty. |
| `push` | boolean | `false` | Whether the caller pushes the packages to NuGet; passed through to the output push. |

| Output | Meaning |
| --- | --- |
| `version` | The version the packages carry. |
| `push` | `'true'` when the packages are to be pushed: the condition of the caller's push job. |

Artifacts: `packages-<rid>` per platform and `packages-portable`, kept 7 days. The caller's push job needs the whole
call, so it runs only when the tests, every platform's package and the portable packages all succeeded.

## Why the push is the caller's job

nuget.org trusted publishing gives a short-lived API key only to an OIDC token whose `repository` is the repository
its trusted publishing policy names and whose `job_workflow_ref` is a workflow file of that same repository. A job
defined inside a reusable workflow of `orldev/Snail.Sdk` carries Snail.Sdk's `job_workflow_ref`, so nuget.org refuses
it for every other repository. An action, on the other hand, runs inside the caller's job, whose `job_workflow_ref` is
the caller's own `publish.yml`. Hence the two jobs of every `publish.yml`:

- `release` calls `lib-publish.yml` or `mcp-publish.yml`: it builds, tests and packs, with no `id-token`.
- `push` needs `release`, runs when `needs.release.outputs.push == 'true'`, holds `id-token: write`, and has one step,
  the `nuget-push` action. It checks out nothing and runs none of the repository's code, so the only job that can get
  a key never runs a test.

The trusted publishing policy on nuget.org names the owner `orldev`, the repository by its current name and the
workflow file `publish.yml`. The secret `NUGET_USER` holds the nuget.org account the policy belongs to, `devorl`.
S3LC0011 holds every `publish.yml` to this shape: the release job without `id-token: write`, the push job with it.

## The nuget-push action

`orldev/Snail.Sdk/.github/actions/nuget-push@v1`, a composite action for the push job of a `publish.yml` (above). It
downloads the artifacts matching `packages`, logs in with `NuGet/login@v1` (an OIDC key for this run only), and runs
[`push.sh`](https://github.com/orldev/Snail.Sdk/blob/master/.github/actions/nuget-push/push.sh): every `*.nupkg` is
pushed to nuget.org with `--skip-duplicate`, a `.snupkg` beside a package goes with it, and a package whose
`DotnetToolSettings.xml` names `RuntimeIdentifierPackages` — the pointer of a native server — is pushed after every
other, since pushed first it would install nothing until the packages it names arrive. A failed push stops the script,
so the pointer never follows a failure; no package found is an error. A run repeated after a partial push completes it.

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `user` | string | required | The nuget.org account the trusted publishing policy belongs to (secret NUGET_USER). |
| `packages` | string | `packages*` | The artifacts holding the packages, as a download-artifact pattern. |

It needs a Linux runner (`runs-on: ubuntu-latest`) and `permissions: { id-token: write, contents: read }` on its job.

## mcp-image.yml

The container image of a server whose project sets `SnailMcpContainer`. Neither the solution nor the tests build the
Dockerfile, so the image is built on every push and pull request, and a broken one shows before a merge. It is pushed
only from a `v*` tag, to `ghcr.io/<owner>/s3l-<short>-mcp`, tagged with the version, `latest` and the short commit;
a prerelease tag (`v1.2.0-preview.1`) gets its version and the commit but leaves `latest` where it is. A branch or pull
request build is tagged with its name and the commit and is not pushed. Images go to GHCR only.

Every build passes `VERSION` as a build argument: the release's version from the tag (`v1.2.0` gives `1.2.0`), or
`0.0.0-<branch>.<commit>` from a branch or pull request, so the server built into the image reports in `initialize`
what it is. The template's `Dockerfile` takes it as `ARG VERSION=0.0.0-local` — what a local `docker build` gets — passes
it to `dotnet publish` as `-p:Version` and labels the image `org.opencontainers.image.version`. A local build is tagged
by hand, `docker build -t s3l-<short>-mcp:local .`, never left to default to `latest`, the name a deployment pulls, and
names its commit when that matters: `--build-arg VERSION=0.0.0-local.$(git rev-parse --short HEAD)`, since the image sees
no git. A `Dockerfile` without the `ARG` only sees a warning that the argument was not consumed.

```yaml
name: server image
on:
  push:
  pull_request:
  workflow_dispatch:
permissions:
  contents: read
jobs:
  image:
    permissions:
      contents: read
      packages: write
    uses: orldev/Snail.Sdk/.github/workflows/mcp-image.yml@v1
```

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `image` | string | `''` | The image name when not empty; s3l-<short>-mcp otherwise, the short name being the repository's name without its Snail.MCP. prefix (any case) and its dots, in lower case. |
| `dockerfile` | string | `Dockerfile` | The Dockerfile, from the repository root. |
| `context` | string | `.` | The build context. |
| `platforms` | string | `linux/amd64` | The platforms to build, as buildx takes them. |
| `build-args` | string | `''` | Build arguments, one NAME=value a line, as the Dockerfile's ARG lines take them. |

Permissions: the job `image` asks for `packages: write`, which the caller grants. A new GHCR package is private; the
owner makes it public once.

## docs.yml

The site on GitHub Pages, on a push to the default branch. The job `build` builds the solution with
`-p:SnailReference=true`, so the site never shows an older reference than the code, runs `prepare` if given, then the
`SnailSite` target on a project of the repository — the same build as on a machine, strict, with the theme the SDK
carries. The job `deploy` publishes it.

```yaml
name: documentation
on:
  push:
    branches: [master]
  workflow_dispatch:
permissions:
  contents: read
jobs:
  docs:
    permissions:
      contents: read
      pages: write
      id-token: write
    uses: orldev/Snail.Sdk/.github/workflows/docs.yml@v1
```

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `solution` | string | `''` | The solution to build; the one .slnx at the root when empty. |
| `project` | string | `''` | A project of the repository that runs the SnailSite target; the first under src/ that has it when empty. |
| `prepare` | string | `''` | A command run after the reference is written and before the site is built, such as one that measures coverage into a page. |

Permissions: the job `deploy` asks for `pages: write` and `id-token: write`, which the caller grants. The repository's
Pages source is GitHub Actions; the workflows share the concurrency group `pages`, so two deployments never overlap.

## perf.yml

Benchmarks and mutation testing, on a schedule (every two months) and by hand before a release that touches a hot
path. The job `benchmarks` runs the benchmark project with `--job short --exporters csv github` and compares the
allocations of each benchmark with the CSV reports kept in `baseline/` beside the project: an allocation that grows past
`allocation-tolerance` fails the job. Only memory fails it, because time on a shared runner moves by tens of percent
between runs and allocations do not; times are reported beside it. The job `mutation` runs Stryker.NET, of the version
the workflow pins, and reports its score; it fails only under `mutation-break`. A repository starts with the
template's baseline and replaces it with the reports of its first accepted run.

```yaml
name: performance
on:
  schedule:
    - cron: '0 4 1 */2 *'
  workflow_dispatch:
permissions:
  contents: read
jobs:
  perf:
    uses: orldev/Snail.Sdk/.github/workflows/perf.yml@v1
```

| Input | Type | Default | Meaning |
| --- | --- | --- | --- |
| `benchmarks` | string | `benchmarks` | The benchmark project, a folder or a .csproj. |
| `benchmark-filter` | string | `*` | The benchmarks to run, as BenchmarkDotNet's --filter takes it. |
| `os` | string | `ubuntu-latest` | The runner the benchmarks run on; the baseline must come from the same kind. |
| `allocation-tolerance` | number | `5` | How many percent an allocation may grow over the baseline. |
| `mutation` | boolean | `true` | Run Stryker.NET. |
| `mutation-break` | number | `0` | The mutation score under which the job fails; 0 reports only. |

Artifacts: `benchmarks` and `mutation` (the reports).

## The MSBuild names the workflows rely on

The workflows read these from a project with `dotnet msbuild -getProperty` or pass them with `-p:`; they are part of
the `v1` contract, and a change to their meaning is a change of major version.

| Name | Read or set by | For |
| --- | --- | --- |
| `AssemblyName` | `mcp-ci.yml`, `mcp-publish.yml` | the program to run for the contract tests |
| `PackageId` | `mcp-publish.yml` | the server's package to look for among those packed |
| `SnailMcpNative` | `mcp-ci.yml`, `mcp-publish.yml` | native per-platform build or one framework-dependent one |
| `ToolPackageRuntimeIdentifiers` | `mcp-ci.yml`, `mcp-publish.yml` | the platforms, one runner each |
| `Version` | `lib-publish.yml`, `mcp-publish.yml` (`-p:Version=`) | the released version, in the packages and the assemblies |
| `SnailReference` | `lib-ci.yml`, `mcp-ci.yml`, `docs.yml` (`-p:SnailReference=true`) | writing the reference |
| `SnailConformance`, `SnailConformanceRelease` | `lib-*`, `mcp-*` | the conformance check, with the release checks in a release |
| `SnailSite`, `SnailSiteProject`, `SnailSiteFolder`, `SnailSiteRepositoryUrl` | `docs.yml` | finding the project that builds the site, and building it |

Each is described in the [MSBuild properties](properties.md).

## Snail.Sdk's own workflows

Snail.Sdk does not call its shared workflows; it has three of its own.

| Workflow | Runs on | Does |
| --- | --- | --- |
| [`ci.yml`](https://github.com/orldev/Snail.Sdk/blob/master/.github/workflows/ci.yml) | every branch push, pull requests | `dotnet test` (the SDK built into packages and used on real projects), actionlint over the shared and template workflows, shellcheck over the action's script |
| [`publish.yml`](https://github.com/orldev/Snail.Sdk/blob/master/.github/workflows/publish.yml) | a tag `vX.Y.Z` | tests, packs every package and pushes them with the `nuget-push` action of the same commit |
| [`site.yml`](https://github.com/orldev/Snail.Sdk/blob/master/.github/workflows/site.yml) | pushes to `master`, pull requests | builds this site strictly with `tools/site.sh` and, from `master`, deploys it to Pages |

No workflow runs on the moving `v1` tag: it points at a commit those workflows have already checked.

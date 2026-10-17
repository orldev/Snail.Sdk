# Releasing

How a Snail library, a Snail MCP server, a server's container image and Snail.Sdk itself are released; what a
repository needs on GitHub and nuget.org before its first release; what counts as breaking; and how a repository moves
from a local package feed to the packages on NuGet. Every release is a version tag on a green commit — the tag is the
version, so a release can never carry a number nobody chose.

## A library or a server

1. **Master is green**: `ci.yml` passed on the commit to release.
2. **Rehearse** (once before a first release, and whenever the release workflow or the platforms changed): run
   `publish.yml` by hand — *Actions → release to NuGet → Run workflow* — with the version to release and *push* left
   unticked. It runs the whole release and keeps every package as an artifact of the run for seven days, and pushes
   nothing. For a native server it is the only way to see the Linux and Windows packages before a release: a Mac cannot
   build them.
3. **Tag and push the tag**:

   ```bash
   git tag v1.2.0          # or v1.2.0-preview.1
   git push origin v1.2.0
   ```

   `publish.yml` starts on tags of the form `vX.Y.Z` (with an optional suffix) and on nothing else, so a moving tag such
   as `v1` releases nothing.

What the release then does:

| Step | Library (`lib-publish.yml`) | Server (`mcp-publish.yml`) |
| --- | --- | --- |
| version | the tag without its `v`, or the input when run by hand; refused unless `X.Y.Z[-suffix]` | the same |
| conformance | `-t:SnailConformance -p:SnailConformanceRelease=true`: the standard, plus nothing left of a local package feed | the same |
| tests | the solution, on the `os` input's runner | the test project |
| packages | `dotnet pack -p:Version=<version>`, validated against `SnailPackageBaseline`; packages and symbol packages | a native server: one tool package per platform of `ToolPackageRuntimeIdentifiers`, each built on its own system, failing on any trim or AOT warning, and asked over stdio where it can run; a framework-dependent fallback (`any`); the package that points at them; `also-pack` projects. A framework-dependent server: one package. `.mcp/server.json` takes the version first, since every package carries it |
| artifacts | `packages` | `packages-<rid>`, `packages-portable` |
| push | the caller's `push` job: the `nuget-push` action, through trusted publishing | the same; the package that points at the platform packages is pushed last |

Every assembly carries the released version (`-p:Version`), so a server reports it in `initialize`. The push job runs
only when the whole release job succeeded, and pushes with `--skip-duplicate`: a run repeated after a partial push
completes it. The pointer goes last because, pushed before the platform packages it names, it would install nothing
until they arrive.

The runners of a native server's platforms: `osx-arm64` on `macos-latest` (`osx-x64`, if a server lists it, is built there but not run),
`linux-x64` on `ubuntu-latest`, `linux-arm64` on `ubuntu-24.04-arm`, `win-x64` on `windows-latest`, `win-arm64` on
`windows-11-arm`.

### An image

A server with `SnailMcpContainer` keeps `.github/workflows/image.yml`, which calls `mcp-image.yml` on every push and pull
request, so a broken `Dockerfile` shows before a merge. The same version tag that releases the packages pushes the image
to `ghcr.io/<owner>/s3l-<short>-mcp` with the version, the commit and `latest`; a prerelease tag
(`v1.2.0-preview.1`) leaves `latest` where it is, since deployments pull `latest`. Builds of branches and pull requests
are never pushed. Every image says what it is from the inside: the workflow passes the version — the tag's, or
`0.0.0-<branch>.<commit>` — as the `VERSION` build argument, the server reports it in `initialize` and the image carries
it as `org.opencontainers.image.version`; a local `docker build`, which gets `0.0.0-local`, is tagged
`s3l-<short>-mcp:local` and never left to default to `latest` ([workflows](workflows.md#mcp-imageyml)). A
`Dockerfile` that never declares `VERSION` would build the server with the version of no release while buildx only
warns, so the conformance check fails it (S3LC0030).

### After a release

- **A library**: move every line of `PublicAPI.Unshipped.txt` but `#nullable enable` into `PublicAPI.Shipped.txt`, and
  set `<SnailPackageBaseline>1.2.0</SnailPackageBaseline>` in `Directory.Build.props`. Package validation then compares
  every later pack with this release; the naming rules and the `Async` suffix rule stop asking anything of the names it
  shipped; and the conformance check requires the baseline once `PublicAPI.Shipped.txt` lists an API (S3LC0019).
- **A server**: nothing to move. The next release takes its version from its own tag.
- **An image's first release**: make the package public once (below).

## First-time setup

What a repository needs on GitHub and nuget.org before its first release. These are settings, not files, so no workflow
can make them.

| Where | What | Why |
| --- | --- | --- |
| nuget.org, account `devorl` | a trusted publishing policy per repository: owner `orldev`, the repository's **current** name, workflow file `publish.yml`, no environment | nuget.org gives a key only to a token whose repository and workflow file the policy names; a renamed repository needs a policy under its new name |
| nuget.org | the package ID prefix `Snail.Sdk` reserved for the account | a package of another account can never take a name of the SDK |
| repository secret | `NUGET_USER` = `devorl` | the account the key is issued for; the push job reads it |
| repository settings → Pages | source *GitHub Actions* | `docs.yml` deploys with the Pages actions, not from a branch |
| environment `github-pages` | a deployment rule for every ref `docs.yml` deploys from: the default branch, and `v*` tags where the site deploys on tags | the environment refuses a deployment from a ref it does not name |
| default branch | `master` | the templates' `docs.yml` deploys on a push to `master`; another default branch changes that line |
| GHCR, the package of an image | visibility *public*, once after the first push | a first push leaves the image private, and a private image needs a login to pull |
| rulesets | on `orldev/Snail.Sdk`: `v*` tags created, moved and deleted only by the owner; `master` protected | the `v1` tag is what every repository's workflows run; whoever moves it changes every release |

**A private repository** is held to more limits: GitHub Pages needs a paid plan; the ARM runners (`ubuntu-24.04-arm`,
`windows-11-arm`) are free for public repositories only, so a native private server passes `runtimes` without those
platforms or pays for the runners; and nuget.org keeps a trusted publishing policy for a private repository active for
seven days until its first publish binds it — a policy that expired unused is renewed on nuget.org.

## Snail.Sdk itself

Every package of the repository — `Snail.Sdk`, `.Analyzers`, `.Library`, `.Mcp`, `.Mcp.Hosting`, `.Mcp.Http`,
`.Mcp.Testing`, `.Templates` — is released together, at one version, by its own `publish.yml`.

1. **Master is green**: `ci.yml` ran the tests, actionlint and shellcheck.
2. **The release commit**: the entries of `src/Analyzers/AnalyzerReleases.Unshipped.md` move to
   `AnalyzerReleases.Shipped.md` under `## Release X.Y.Z`.
3. **Tag**: `git tag vX.Y.Z && git push origin vX.Y.Z`. `publish.yml` tests, packs every package at `X.Y.Z` and pushes
   them with the `nuget-push` action of the commit being released.
4. **Wait for nuget.org to index them**: the version must be listed at
   `https://api.nuget.org/v3-flatcontainer/snail.sdk/index.json` (and the same for each package id, in lower case)
   before anything restores it.
5. **Move `v1`**, only when the shared workflows and the action at `vX.Y.Z` work with every Snail.Sdk version a `@v1`
   caller may pin in its `global.json`:

   ```bash
   git tag -f v1 vX.Y.Z
   git push -f origin v1
   ```

   The moving tag starts no workflow: `publish.yml` needs `X.Y.Z` and `ci.yml` runs on branches only.
6. **After the release**: move the lines of `PublicAPI.Unshipped.txt` of `src/Mcp.Hosting`, `src/Mcp.Http` and
   `src/Mcp.Testing` into their `PublicAPI.Shipped.txt`; set their `PackageValidationBaselineVersion` to the release (the
   first release sets it for the first time); raise `VersionPrefix` in `Directory.Build.props` to the next version, so
   that local packs (`X.Y.Z-local.N`) sort above the release.

**`v1` is not a package version.** The packages start at 0.1.0 and follow SemVer on their own. `v1` is the major version
of the contract of the shared workflows and the `nuget-push` action — their names, inputs, outputs, secrets, artifacts
and permissions — and callers write `@v1`. The tag moves to the commit of a package release whose workflows keep every
`@v1` caller working. A workflow change that would break a caller is released under `v2`: a new tag from that release on,
while `v1` stays where it is. Dependabot proposes `@v2` to the callers when it exists.

## What counts as breaking

**For a `@v1` caller** — a change of this kind needs `v2`:

- a shared workflow or the action renamed or removed; an input, output, secret or artifact name (`packages`,
  `packages-<rid>`, `packages-portable`) renamed or removed; an input made required;
- an input's default changed so that a caller's run does something else;
- a permission the caller has to grant that it did not grant before;
- a workflow relying on an MSBuild name or behaviour that a Snail.Sdk version a caller may pin does not have. The
  workflows read `AssemblyName`, `PackageId`, `SnailMcpNative`, `ToolPackageRuntimeIdentifiers` and `SnailSiteProject`,
  and use `SnailReference`, `SnailSite`, `SnailConformance` and `SnailConformanceRelease`: these names are part of the
  `v1` contract.

**For a project on the packages** — a breaking release of Snail.Sdk:

- a public MSBuild property, target, item or metadata removed, renamed or changed in meaning (names starting with `_`
  are private and change freely);
- a new required conformance check, or advice becoming required;
- a new analyzer rule at warning or error, or a rule's severity raised; a new or stricter `ToolGuards` rule;
- the .NET SDK band in `global.json`, or the target framework;
- a major version of the MCP SDK the server libraries are built against;
- a public C# API of `Snail.Sdk.Mcp.Hosting`, `.Http` or `.Testing` removed or changed (their `PublicAPI.Shipped.txt`).

While the packages are below 1.0, a breaking release raises the minor version (0.1 → 0.2) and anything else the patch;
from 1.0 on, a breaking release raises the major, new optional inputs, outputs, advice checks, suggestions and APIs raise
the minor, and fixes that keep every name and default raise the patch.

## From the local feed to NuGet

A repository built on an unreleased Snail.Sdk takes it from a local feed (`LocalPackages`, see
[getting started](getting-started.md)). Once the release is on nuget.org:

1. Wait until nuget.org indexes the version (step 4 above).
2. In `global.json`, set every `msbuild-sdks` entry to the released version.
3. In `nuget.config`, remove the `LocalPackages` mapping, and the `LocalPackages` source if the file adds one.
4. In `Directory.Packages.props`, no `-local` version may remain: a package of another Snail repository moves to its
   release the same way, after that release.
5. Restore and check:

   ```bash
   dotnet build -c Release
   dotnet test -c Release
   dotnet msbuild src/<Project>.csproj -t:SnailConformance -p:SnailConformanceRelease=true
   ```

   The release checks (S3LC0026) fail on anything left of the local feed — the same checks the release workflow runs,
   so a release can never rest on packages only one machine has.
6. Commit.

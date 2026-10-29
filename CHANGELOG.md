# Changelog

What changed in each release of Snail.Sdk, for the repositories that build on it. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow [Semantic Versioning](https://semver.org/).
A change that a repository must act on says what to do.

## [Unreleased]

### Fixed

- S3LC0032 reads what the model reads — the `Description` attributes, and the strings of the files under `Prompts/` —
  and nothing else: a comment, a log line or a literal elsewhere that says "poll" is the program's own. 0.4.0 named
  them all.
- A job's state is written in JSON as its lower-case word (`running`, `succeeded`, `failed`, `cancelled`), the way a
  state reads everywhere else, in a tool's answer as in the job's resource; 0.4.0 wrote `Running`.
- A `subscriptions/listen` request that asks for nothing is acknowledged with nothing: the acknowledgement names no
  notification kind, as a client that opens the stream beside `tools/list` expects. 0.4.0 answered it with an empty
  `resourceSubscriptions` list.

## [0.4.0] - 2026-10-29

### Added

- A long operation as a job. `Jobs` of `Snail.Sdk.Mcp.Hosting`, registered with `.WithJobs("<short>")`: a tool starts
  the work with `Jobs.Start`, takes `int wait = 60` and answers within it — the result, or the running job as the
  resource `job://<short>/<kind>/<id>` whose text is its report. The server advertises `resources.subscribe`, tells
  every subscriber of each change over `resources/subscribe` (up to the 2025-11-25 revision) or the
  `subscriptions/listen` stream (from 2026-07-28, held open), and cancels its running jobs when the host stops.
- `Snail.Sdk.Mcp.JobClient`, the client side: `CallJobAsync`, `AwaitJobAsync`, `WatchJobAsync` and `ReadJobAsync` on
  `McpClient` wait for a job by being told, reading the resource again only when nothing was told within a fallback. A
  test project on `Snail.Sdk.Mcp` references it implicitly, beside `Snail.Sdk.Mcp.Testing`.
- `ToolGuards` names a description that tells the model to poll ("poll", "call again", "check back"): the model is not a
  timer. S3LC0032 *jobs* (advice): a server whose tools take a `wait` registers its jobs, keeps no `*_status` tool
  beside them, and says nothing of polling.
- `dotnet new s3l-mcp --jobs true`: the sample job tool `<short>_work`, its registration and a contract test that
  awaits it.

## [0.3.0] - 2026-10-29

### Changed

- The server libraries move to ModelContextProtocol 2.3.0, and `Snail.Sdk.Mcp.Hosting` requires it. A server that pins
  `ModelContextProtocol` itself raises it to 2.3.0 with this release, or the restore fails on a downgrade (NU1605). In
  2.3.0 `McpClient.ServerInfo` may be null, so a test reading `client.ServerInfo.Name` no longer builds (CS8602): read
  `client.ServerInfo?.Name`, or assert it is not null first. The `s3l-mcp` template does so.
- Microsoft.Extensions.Hosting and the other .NET packages move to 10.0.14; the templates take Microsoft.NET.Test.Sdk
  18.11.0, xunit.runner.visualstudio 4.0.1, Microsoft.AspNetCore.Mvc.Testing 10.0.14 and Snail.Toolkit.RabbitMQ 2.1.0.

### Fixed

- S3LC0019 reads `SnailPackageBaseline` where MSBuild does — the packed project, or the `Directory.Build.props` above it.
  A library that named its baseline in its project file was refused its release.

## [0.2.2] - 2026-10-27

### Fixed

- A native server that builds an image packs its pointer package again. `SnailMcpContainer` kept `PublishAot` off
  for every build without a runtime, `dotnet pack` of the pointer included, and packed framework-dependent the .NET SDK
  built a package for every runtime of `ToolPackageRuntimeIdentifiers` in one restore and failed with NETSDK1047: the
  first release of such a server would have stopped there. It now holds for publishing only. A repository that worked
  around it with `PublishAot` of its own in the project — Snail.MCP.Documents — may drop that line.

## [0.2.1] - 2026-10-24

### Fixed

- A server on another SDK is packed as a tool again. `PackAsTool`, the server's name and its tool command moved from
  `Snail.Sdk.Mcp.targets` to `Snail.Sdk.Mcp.props`: the .NET SDK imports its tool packaging from
  `Microsoft.NET.Sdk.targets` under a condition on `PackAsTool`, so a project that takes the Razor or Web SDK and adds
  this one with an `<Sdk>` element set it too late and the import never happened. Such a package carried no `tools`
  folder, no `DotnetToolSettings.xml` and no `DotnetTool` package type, and nuget.org refused it. A repository that
  worked around this in its own project file — `PackAsTool` or `ToolCommandName` of its own — may drop those lines.

## [0.2.0] - 2026-10-24

### Added

- ASP.NET applications as a kind of the standard. A repository whose own project builds on `Microsoft.NET.Sdk.Web` with
  `<Sdk Name="Snail.Sdk" />` is checked as an application: no public API, package baseline, native probe or reference
  is asked of it, and it is released as its image (S3LC0025, S3LC0030).
- `dotnet new s3l-web`: a minimal API or Blazor Server pages styled with Tailwind, behind a proxy, limited per client,
  with `/health`, tests, a benchmark, an image with tini as process 1, and docker compose or a Nomad job behind traefik.
  Options: `--kind`, `--auth`, `--mongo`, `--rabbit`, `--vault`, `--deploy`, `--localization`.
- `--localization` of `s3l-web`: every sentence a person reads in `Resources/Messages.resx`, with Russian and Spanish
  beside it. The application speaks every language whose `Messages.<language>.resx` it was built with, and answers in
  the language chosen (a cookie), then the browser's, then English.
- The shared workflow `app-ci.yml`: an application's tests and conformance check.
- S3LC0031 *translations*: every `Name.<culture>.resx` holds exactly the keys of the English `Name.resx` beside it, none
  empty, with the same placeholders. A repository whose translations have drifted fails the check until they agree.
- [Getting started](docs/getting-started.md#an-existing-repository): how to bring an existing repository onto Snail.Sdk.

### Fixed

- S3LC0019 no longer fails the first release of a library: the tag being released is not taken for an earlier
  release. The workarounds — releasing by `workflow_dispatch`, or `DisablePackageBaselineValidation` — can go.
- The SDK's own tests pack above the last release, so package validation holds, and take what they restored out of
  the package cache.

## [0.1.0] - 2026-10-17

The first release.

### Added

- `Snail.Sdk`, `Snail.Sdk.Library` and `Snail.Sdk.Mcp`: the MSBuild SDK levels of a project, a library and an MCP server —
  language, nullable, documentation, warnings as errors, the analyzers and their configuration, package validation,
  SourceLink, Native AOT for servers and the native probe of a library.
- `Snail.Sdk.Analyzers`: the rules S3L0001–S3L0010, with a code fix where the fix is mechanical.
- The conformance check, S3LC0001–S3LC0030: the files, workflows, projects and images of a Snail repository.
- `Snail.Sdk.Mcp.Hosting`, `Snail.Sdk.Mcp.Http` and `Snail.Sdk.Mcp.Testing`: the host of a server over stdio and over
  loopback HTTP, its settings and folders, and the guards of its tool list.
- `Snail.Sdk.Templates`: `dotnet new s3l-lib` and `dotnet new s3l-mcp`.
- The shared workflows, called at `@v1`: `lib-ci`, `mcp-ci`, `lib-publish`, `mcp-publish`, `mcp-image`, `perf` and
  `docs`, and the `nuget-push` action for trusted publishing.

[Unreleased]: https://github.com/orldev/Snail.Sdk/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/orldev/Snail.Sdk/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/orldev/Snail.Sdk/compare/v0.2.2...v0.3.0
[0.2.2]: https://github.com/orldev/Snail.Sdk/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/orldev/Snail.Sdk/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/orldev/Snail.Sdk/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/orldev/Snail.Sdk/tree/v0.1.0

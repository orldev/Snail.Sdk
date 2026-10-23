# Changelog

What changed in each release of Snail.Sdk, for the repositories that build on it. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow [Semantic Versioning](https://semver.org/).
A change that a repository must act on says what to do.

## [Unreleased]

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

[Unreleased]: https://github.com/orldev/Snail.Sdk/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/orldev/Snail.Sdk/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/orldev/Snail.Sdk/tree/v0.1.0

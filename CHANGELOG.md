# Changelog

What changed in each release of Snail.Sdk, for the repositories that build on it. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the versions follow [Semantic Versioning](https://semver.org/).
A change that a repository must act on says what to do.

## [Unreleased]

### Added

- `HttpServerProgram` of `Snail.Sdk.Mcp.Testing`, the HTTP twin of `ServerProgram`: the build beside the tests or the
  publication the override variable names — a native program among them — started on a free loopback port with its
  own home and no settings file, and handed back once `GET /healthz` answers with success. The address goes into
  `SNAIL_MCP_<SHORT>_URL` unless `addressVariable` names another (given the port alone when its name ends in `_PORT`);
  `readiness: null` waits for the MCP endpoint of a server that maps no readiness path. A server that exits at start
  fails the test at once with its exit code and its stderr, rather than after the minute of a loop; `Log` is its
  stderr so far, `SessionAsync(query)` a session on `/mcp?<query>`, and disposing it kills it with every process it
  started. `Ports.Free()` is a free loopback port, and `HttpGuards.ProblemsAsync(endpoint)` the refusal of a browser
  checked of the running server — a request with `Origin` (a page's, and `null`) answered 403, and one without it
  not, since an endpoint that refuses everyone proves nothing. A server's HTTP or process contract test may replace
  its own start, `/healthz` loop, kill, `SessionAsync` and `FreePort()` with
  `await HttpServerProgram.StartAsync("Snail.MCP.<Name>", "SNAIL_<SHORT>_SERVER", environment)` in `InitializeAsync`
  and `await server.DisposeAsync()`, and its browser test with
  `Assert.Empty(await HttpGuards.ProblemsAsync(server.Endpoint))`; `dotnet new s3l-mcp`'s `HttpContractTests` does.
- One answer envelope for every tool, in `Snail.Sdk.Mcp.Hosting.Answers`. `ToolAnswer.Ok(data, shape)`,
  `ToolAnswer.Failed(error, hint, type, data)` and `ToolAnswer.From(answer, data, shape)` write the text every server
  of the fleet writes by hand — one block of compact JSON with nothing escaped that JSON allows, `{"ok":true,"data":…}`
  or `{"ok":false,"error":…,"hint":…,"type":…,"data":…}` — with these rules: `ok` first; a field without a value left
  out (the servers wrote `"hint":null`); a failure always `isError` and text alone, with no structured content, since a
  client of the TypeScript SDK validates the structured content of an error against the tool's output schema; a
  success's structured content is its data (`AnswerShape.Data`, the default), the whole envelope
  (`AnswerShape.Envelope`, for tools with an output schema) or none (`AnswerShape.None`). `Answer<T>` with
  `Answer.Ok(value)` and `Answer.Failed<T>(error, hint, type)` is the value-or-why a server's application layer returns;
  success is `IsSuccess`, its error being null, never its value being set — `Value is { }` holds for a failed
  `Answer<bool>` too. `ToolAnswer.IsFailure(result)` tells the failure envelope by its top-level `ok`;
  `ToolAnswer.Word(value, WordCase.Lower | WordCase.Camel)` spells an enum value (`inprogress`, `inProgress`);
  `ToolAnswer.Moment(at)` a moment, round-trip and invariant; `ToolAnswer.Wire` are the options and
  `ToolAnswer.ObjectInfo` the generated `JsonObject`, AOT-safe. To adopt: delete the server's `ToolResponse` envelope
  methods, `Answer` record and the `JsonSerializerContext` it kept only for `JsonObject` (`AnswerJson`, `E2EJson`,
  `ConnectorJson`), call `ToolAnswer` instead, pass `ToolAnswer.ObjectInfo` to `Jobs.Start` for a `JsonObject` result,
  and expect `"hint":null` to be gone from the text. A server whose bridge errors carried `data.type` (AfterEffects,
  Blender) passes `type:` and says `type` instead of `data.type` in its instructions.
- `.ExplainFailures()` on `IMcpServerBuilder`: a tool that throws is answered with the failure envelope — its message, a
  hint, `type` `internal` — and logged once with its trace, instead of the MCP SDK's bare "An error occurred invoking
  …"; and a result whose text is the envelope with `ok` false but no `isError` — a tool that returns the envelope as a
  string — is flagged. It goes first among the call filters wherever it is called; a call its client cancelled stays
  cancelled, and an `McpProtocolException` stays a protocol error. A server with a filter of its own for either
  (Snail.MCP.Documents' `ExplainFailures`, Snail.MCP.Figma's `FlagFailures`) calls this instead.
- `JobAnswer.Envelope(report, beside)`: a job in the envelope — the report as the text's data, with the fields of
  `beside` added there, and the report alone as the structured content a client of `Snail.Sdk.Mcp.JobClient` reads.
  Replaces `ToolResponse.Job(report)` of Snail.MCP.Security, BuildBridge, AfterEffects and Blender.
- `Snail.Sdk.Mcp.Testing` reads the envelope: `answer.IsOk()`, `Data()`, `Error()`, `Hint()` and `Report()` (the job's
  report in the structured content), failing with the text when it is not the envelope. A test that parsed the text
  itself (`ReportOf`, its own `Ok`/`Data` readers) uses these. The package now depends on `Snail.Sdk.Mcp.Hosting`.
- `AddIdleShutdown(after, isBusy)` of `Snail.Sdk.Mcp.Hosting`: a stdio server that has heard no MCP message for
  `after` stops, unless `isBusy` says it is at work no message shows. For the process a client started and then lost
  without closing its stdin, which waited for ever. Any MCP message counts, over the filter the idle trim counts with —
  the watchers Snail.MCP.Blender, AfterEffects, Figma and Memory kept of their own counted `tools/call` alone, so a
  client that only pings now keeps the server up. A server served over HTTP is never stopped, so a program of both
  transports registers it alike. `TimeSpan.Zero` turns it off; the period and its default stay the server's setting.
  A server drops its `IdleShutdownWatcher`, `IActivityTracker` and their registration for one call.
- `McpEndpoint.Read(settings, defaultUrl)` and `McpTransport`: where a server answers, from either of the two ways the
  fleet says it — `Transport` with `Url` (or `Http:Url`), or a `Port` alone — on every interface inside a container
  (`DOTNET_RUNNING_IN_CONTAINER=true`), with `ListensOnEveryInterface`, or at `0.0.0.0`, on loopback anywhere else. A
  value a key cannot mean is refused at the start, naming it. `McpHttpHost.CreateBuilder(args, endpoint)` builds the
  host it names, loopback or the container's. A server drops its `McpServerHost` choice and its `Transport` enum.
- `McpHttpHost.MapHealth(path = "/healthz")`: 200 with no body, for a container's healthcheck, which the servers in
  images mapped by hand.
- `ServerSecret` of `Snail.Sdk.Mcp.Hosting`: `ReadOrCreate(file, length)` keeps a secret in the server's data folder —
  readable by its owner alone from its first byte, written to a draft and moved into place whole, made by one process
  at a time under a lock beside it so every server starting at once agrees on one, and never replaced: a file of
  another length is refused with its name. `ReadOrCreateHex` keeps it as hex text, for a token another program reads
  (Snail.MCP.Figma's `bridge.token`, Snail.MCP.AfterEffects' bridge). `Matches` compares in constant time. A move
  "without overwrite" alone does not guard the race on Unix, where .NET looks for the target and then renames over it.
- `ServerFolders.Home`, now public, and `ServerFolders.ExpandHome(path)` / `ExpandHome(path, fallback)`: a leading `~`
  or `~/` made the user's home; a missing path is the fallback. The servers kept copies of their own in nine places.
- What a session's address grants it, for every server — stdio and HTTP alike — in `Snail.Sdk.Mcp.Hosting.Sessions`.
  Nine servers kept a `SessionGrant` of their own, four a `SessionRoots`, three a `SessionCopy`, and three let a role
  granted `run_tools` reach every tool they had, because their copies held at the door and nowhere else. A server that
  keeps one of those deletes it and its `ISessionAddress`, `HttpAddress` and `StdioAddress`, and takes these instead:
  - `.RefuseWhatWasNotGranted()` beside `AddMcpServer`: a call to a tool the address (`?tool=`) did not name is refused
    in the fleet's words, and the tool is left out of the list (`GrantOptions.HidesWithheldTools`, true). A server whose
    answers are an envelope of its own shapes the refusal with `GrantOptions.Refusal`; one that must never serve a session
    without a grant sets `GrantOptions.RequiresGrant` (HTTP only: a program over stdio names no tool). A server with
    scopes of its own — Snail.MCP.AfterEffects, Blender — registers an `IToolGate`, which the door asks instead.
  - A forwarder — `run_tools`, `run_program` — carries `[Forwards]` and calls each step through
    `ToolForwarding.CallAsync(context, tool, arguments, cancellationToken)`: the step is held to the same gate and run as
    the protocol would, and a forwarder is never a step of another. A forwarder whose steps are commands rather than tools
    (Snail.MCP.Git) asks `ToolForwarding.Refusal(context, "git_status")` for each. Its own `Refusal` and `Invoke` go.
  - `.WithSessionRoots(services => volumes)` registers `SessionRoots`: the roots the address names (`?root=`) inside the
    configured volumes, or the volumes when it names none, and `Reach(path)`, which answers the path with every link on it
    followed, or why it may not be used. `RealPath.Of(path)` is the walk alone, and follows a link whose target does not
    exist yet, which every fleet copy took for the end of the path — a write through such a link landed outside the root.
  - `WorkspaceCopy.Of(root, workspaces, isTaskId)` is the common core of the three `SessionCopy`: the task a copy under
    the workspaces folder belongs to, or null.
- `WithSessionAddress()` of `Snail.Sdk.Mcp.Http` reads the session's address from the query of each request;
  `WithLocalHttpTransport()` adds it by itself, and a server on the MCP SDK's default transport calls it beside
  `WithHttpTransport()`. `app.RefuseSessionsItCannotServe(services => volumes)` replaces the `RefuseASessionItCannotServe`
  of Snail.MCP.FileSystem, Documents, Git and Roslyn, with their words: 400 for a root this machine does not have, a root
  outside the volumes, or no root where no volume is configured — after a 403 to a request with an `Origin`, whichever of
  it and `MapLocalMcp` is called first.
- `FixedAddress` of `Snail.Sdk.Mcp.Testing`: a session's address in a test, `new FixedAddress(("tool", "read_file"))`.
  The package now references `Snail.Sdk.Mcp.Hosting`.

### Changed

- The folders are moved from the snail names by reading the settings: `AddServerSettings(name)` — and with it
  `ServerSettings.Read(name)`, `McpHost.CreateBuilder` and both HTTP builders of a program built on Snail.Sdk.Mcp —
  calls `ServerFolders.MoveFromSnailNames(name)` first, so a settings file left under the snail name is read by the
  start that moves it. A server may drop its own `ServerFolders.MoveFromSnailNames` call from `Program.cs`; one that
  keeps it moves nothing more.

## [0.4.2] - 2026-11-06

### Added

- A job may belong to an owner. `Jobs.Start(kind, owner, work, resultType)` starts one, and `.WithJobs("<short>", ownerOf)`
  names the owner of a request from the services it is served with; a job of another owner is then unknown to a read,
  a subscription and a listen stream, as if it did not exist. For a server many clients share — one HTTP server for
  every task of a platform — where one client must not read another's job. `Jobs.Find(uri, owner)` and `Job.Owner` are
  the same check in code. A server that adds no owner works as before.
- `JobAnswer.Of(report, said)`: a tool that answers in words of its own — an `ok` flag with `data`, a text the model reads
  as it always has — keeps them, and the job's report goes beside them as the structured content, where a client of
  `Snail.Sdk.Mcp.JobClient` finds it. Returning the report alone made it the text too.

### Fixed

- A native publish on the 10.0.3xx band passes the package audit again. CVE-2026-111497 (GHSA-p3jp-9v83-76w9,
  3 November 2026) names the AOT compiler the band bundles, `Microsoft.DotNet.ILCompiler` 10.0.10, and no SDK of the
  band carries the fix, so a server's image, the CI's native builds and a library's probe all failed on NU1903. The
  band's compiler entry is raised to the patched 10.0.14 from NuGet while the band bundles an older one; an SDK that
  brings the fix turns it off by itself, and `SnailILCompilerVersion` names another compiler.
- A job's URI ends in a random id, not a count: `job://<short>/<kind>/<32 hex digits>`. A client of a shared server read
  another's jobs — their test output, their logs — by counting from its own. A client that kept a URI keeps working; one
  that built a URI from a number, which nothing in the SDK does, no longer finds the job.
- S3LC0032 keeps a status tool that says where something other than a job stands: the server names it, with its reason,
  in `<SnailStatusToolsBecause>e2e_status: where the environment stands</SnailStatusToolsBecause>`. Snail.MCP.E2E was
  advised against `e2e_status`, which reports whether the app runs, not a job; its description is a constant, which the
  check does not read, so the name has to be said.
- Ended jobs are kept per owner: the last twenty of each (`Jobs.Kept`), and five hundred of the server in all
  (`Jobs.KeptInAll`). Counted for the whole server, twenty ended jobs of other tasks forgot a task's result before it
  was read — on a stand of a few dozen tasks, within minutes.
- A step the work reports is in the job's report at once. It went through `Progress<T>`, which posts it to be handled
  later: the first answer of a running job carried no step, and a step reported just before the end could land after it.

- A native server on the Web or Razor SDK — `Sdk="Microsoft.NET.Sdk.Web"` with `<Sdk Name="Snail.Sdk.Mcp" />` — is
  native again. Snail.Sdk.Mcp decided `PublishAot`, the generated binders and `IsAotCompatible` in its targets, which
  such a project imports after the .NET SDK's, and the .NET SDK reads all three while it is evaluated:
  - published for a runtime, the server came out framework-dependent;
  - a build with an endpoint or bound settings failed on CS9137, the binders' interceptors not granted;
  - the AOT and trim analyzers stayed off, so no build warned of what a native one breaks, and S3LC0016 passed on
    nothing.

  They are now decided in `Snail.Sdk.Mcp.Native.targets`, which such a project gets through
  `BeforeMicrosoftNETSdkTargets`, before the .NET SDK's targets; a server on Snail.Sdk.Mcp alone gets it as before.
  A server that worked around this in its project — `PublishAot`, `IsAotCompatible`,
  `EnableConfigurationBindingGenerator`, `EnableRequestDelegateGenerator` or `InterceptorsNamespaces` of its own, as
  Snail.MCP.Security does — may drop those lines.

  A native server on the Web SDK now builds with the analyzers on, and their IL2xxx and IL3xxx warnings fail the
  build. Fix what they name — a `JsonSerializerContext` for JSON on reflection — or set `SnailMcpNative` to false if
  the server cannot be native.
- `Snail.Sdk.Mcp.JobClient` waits for a job of a server without sessions — the MCP SDK's default HTTP transport — and
  stops waiting when cancelled. The watch ended its `subscriptions/listen` request by sending `notifications/cancelled`
  and waiting for the server to close the stream; a server without sessions gets that cancellation as a request of its
  own, cannot tie it to the stream it holds, and never closed it. `CallJobAsync` and `AwaitJobAsync` hung, past their
  own cancellation. The watch now ends the request on its own side once the cancellation is sent.
- The native `Dockerfile` of `dotnet new s3l-mcp --container true` builds on an arm64 machine. It published for
  `linux-x64` alone, and ahead-of-time compilation does not cross architectures in the SDK image: on a Mac with Apple
  silicon the link failed. It now publishes for the platform the image is built for, from buildx's `TARGETARCH`. A
  repository made from an earlier template may take the same two lines (`ARG TARGETARCH` and the `case` that picks
  the runtime).
- `perf.yml` compares allocations again. BenchmarkDotNet separates the columns of its CSV by the machine's culture —
  `;` on a machine whose decimal mark is a comma, `,` on the runner — and the comparison read `;` only: a runner's
  report gave no allocation, nothing was compared, and the job passed whatever grew (a report with every allocation
  ten times larger passed). The comparison now reads either separator, and a report it reads nothing from fails the
  job. A baseline written on either kind of machine stays valid. Once `v1` moves, a repository whose allocations did
  grow past the tolerance since its baseline sees the job fail for the first time.
- A level references the server libraries of its own release, and refuses a Snail.Sdk of another one. A build resolves
  an SDK once, so when global.json named Snail.Sdk 0.2.2 and Snail.Sdk.Mcp 0.4.1 — Dependabot bumps each in a pull
  request of its own — every project got Snail.Sdk 0.2.2, the server libraries were asked for at 0.2.2, and restore
  failed on `Snail.Sdk.Mcp.JobClient` 0.2.2, which never existed (Snail.MCP.Blender, 1 November 2026). Snail.Sdk.Mcp and
  Snail.Sdk.Library now name their release in `SnailLevelVersion`, and a build whose Snail.Sdk is of another release
  stops before restore with both versions named. A repository names the same release for every entry of `msbuild-sdks`.
- S3LC0019 reads a property through the imports of a nested Directory.Build.props, in MSBuild's order. It read the file
  nearest the project alone, so a repository whose src/Directory.Build.props imports the root's, where
  `SnailPackageBaseline` is named, was told it names no release (Snail.Toolkit.Web, 1 November 2026).

### Changed

- The templates' `perf.yml` runs twice a year, at 04:00 UTC on the first of January and of July, instead of every two
  months. A repository made from an earlier template may take the same cron, `'0 4 1 1,7 *'`.
- The templates' Dependabot brings Snail.Sdk and its levels in one pull request of their own, a major update too, in a
  group `snail-sdk` ahead of `packages`. A repository made from an earlier template may add the same group.

## [0.4.1] - 2026-10-29

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

[Unreleased]: https://github.com/orldev/Snail.Sdk/compare/v0.4.2...HEAD
[0.4.2]: https://github.com/orldev/Snail.Sdk/compare/v0.4.1...v0.4.2
[0.4.1]: https://github.com/orldev/Snail.Sdk/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/orldev/Snail.Sdk/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/orldev/Snail.Sdk/compare/v0.2.2...v0.3.0
[0.2.2]: https://github.com/orldev/Snail.Sdk/compare/v0.2.1...v0.2.2
[0.2.1]: https://github.com/orldev/Snail.Sdk/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/orldev/Snail.Sdk/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/orldev/Snail.Sdk/tree/v0.1.0

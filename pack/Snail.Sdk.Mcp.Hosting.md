# Snail.Sdk.Mcp.Hosting

What every Snail MCP server starts from:

- `McpHost.CreateBuilder(args)`: the empty host, with log lines on stderr (`UseStderr`) since stdout carries JSON-RPC,
  at the levels the server's settings and an image's `Logging__LogLevel__…` variables name, the MCP SDK's from warnings
  up unless they say otherwise (`UseLevelsFrom`), and the heap compacted once the server has heard no MCP message for
  five minutes (`AddIdleTrim`; `SNAIL_MCP_IDLE_TRIM` sets the seconds, `0` turns it off). `McpHost.CreateBuilder(args,
  settings)` builds it over the settings the program has read already, rather than reading them again;
  `AddReadyThreads(workers)` lets the thread pool grow at once for a server that blocks many threads.
- `McpEndpoint.Read(settings, defaultUrl[, fallbackSection])` and `ReadHttp`: where a server answers, stdio or HTTP on
  loopback or every interface, a refusal naming the operator's variable.
- `ToolJson.Options(MyJson.Default)`: the MCP SDK's JSON options with the server's source-generated types put first,
  so the tools work in a native build.
- `ServerSettings.Read(name)` and `configuration.AddServerSettings(name)`: the server's settings file — the one
  `SNAIL_MCP_<NAME>_CONFIG` names, and only that one, or else the first of `./.s3l-mcp-<name>.json`,
  `./s3l-mcp-<name>.json`, `~/.config/s3l-mcp-<name>/config.json` and `~/.s3l-mcp-<name>.json` that exists — then its
  `SNAIL_MCP_<NAME>_` variables, which win. Both host builders add them by themselves for a program Snail.Sdk.Mcp named.
- `EnvironmentSettings.Read(prefix, variables)`: settings from `PREFIX_SECTION__FIELD` environment variables.
- `ServerFolders.DataDirectory(name)` and `ServerFolders.ConfigDirectory(name)`: `~/.s3l-mcp-<name>` and
  `~/.config/s3l-mcp-<name>`; `ServerFolders.ExpandHome(path)` and `FullPath(path)` for a path a person wrote.
- `ServerIdentity.Of(assembly)`: the name and version the server announces in `initialize`.
- `Invocation.Stable(path)`: the path of the running program that outlives an update of it.
- `ToolAnswer.Ok(data)`, `ToolAnswer.Failed(error, hint, type)` and `ToolAnswer.From(answer, data)`: the answer envelope
  every tool gives, `{"ok":true,"data":…}` or `{"ok":false,"error":…,"hint":…,"type":…}` as one compact text block, a
  failure flagged `isError`; `ToolAnswer.Ok(data, typeInfo)` writes typed data without a tree of nodes, and
  `ToolAnswer.Text`/`ToolAnswer.Of` carry the envelope outside a result and back; `result.WithImage(bytes, mime)` adds
  a picture after it, and `Envelope<T>` is its type for an output schema. `Answer<T>` with `TryGet`, `As<TOther>()` and
  `Map` is the value-or-why behind it, `Answer.Failed(error, hint, type, details)` a failure of any `Answer<T>` with what
  was learned before it. `.ExplainFailures()`, in `Snail.Sdk.Mcp.Hosting` itself, answers a tool that threw with it, a
  call whose arguments did not bind with `type` `arguments`, and a `ToolRefusalException` in its own words.
- `Jobs.Start` and `JobAnswer`: a long operation as a job; work that returns an `Answer<T>` ends a failure as a failed
  job with its hint and type, and `JobAnswer.Envelope` or `JobAnswer.Result` answers the tool — `JobAnswer.Pending` a
  job still running.
- `Snail.Sdk.Mcp.Hosting.Sessions`: what a session's address (`?tool=`, `?root=`) grants it.
  `.WithSessionGrant()` refuses and hides a tool the session was not granted (`SessionGrant`, or a gate of the
  server's own, `IToolGate`); `ToolForwarding.CallAsync` is how a `[Forwards]` tool such as `run_tools` calls a step,
  held to the same grant, and `ToolForwarding.Refused` gives a catalog of commands the door's refusal, type
  `notGranted`; `SessionRoots` and `RealPath` say whether a path lies in the session's roots with every link
  followed, and whether it is the file or folder a tool needs — a refusal with its kind, `reached.As<T>()` as a tool's
  failure — on the machine's disk or the server's own (`IRootsDisk`); `WorkspaceCopy.Of` names the task a working copy belongs
  to, or why a root is none; `JobOwners.TaskOfCopy` owns a server's jobs by that task; `SessionAddress.None` is the
  address of a program.
- `Snail.Sdk.Mcp.Hosting.Discovery`: the ways in every server spells alike. `.WithToolDiscovery()` adds `find_tool`,
  the server's tools ranked against a task in words with what each takes; `.WithToolSequences()` adds `run_tools`,
  several tools in one call under the session's grant; `.WithArgumentCheck()` names a missing or unknown argument and
  the parameter it meant; `.WithSkills()` with `[Skill]` lists a group of tools to a session only once it loads it.

A server built on [Snail.Sdk.Mcp](https://www.nuget.org/packages/Snail.Sdk.Mcp) references it implicitly.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

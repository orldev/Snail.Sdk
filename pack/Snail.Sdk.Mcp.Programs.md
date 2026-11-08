# Snail.Sdk.Mcp.Programs

`run_program` for a Snail MCP server: a small JavaScript program, run on [Jint](https://www.nuget.org/packages/Jint)
beside the server, that calls the server's commands itself — `git.status()`, `blender.add_primitive({ kind: "cube" })` —
with loops, conditions and the results in hand. Work of twenty steps that decides as it goes costs one call and one
reply instead of twenty.

A server opts in: a reference without a version (the `Snail.Sdk.Mcp` level gives it its own release) and one line.

```xml
<PackageReference Include="Snail.Sdk.Mcp.Programs" />
```

```csharp
builder.Services.AddMcpServer()
    .WithTools<NoteTools>()
    .RefuseWhatWasNotGranted()
    .WithPrograms<ToolCommands>(options => options.Global = "notes");
```

- `WithPrograms<TCommands>(configure)` registers `run_program(code, input, timeoutSeconds)`, marked `[Forwards]`.
  `TCommands` is an `IProgramCommands` — the server's own catalog, its names and the tool each stands for, each command
  handed the request of `run_program` when it needs the session or its services — or `ToolCommands`, every tool of the
  server but the forwarders, `notes_read` called as `notes.read()`.
- The session's gate is asked for every command's tool before it runs; a refusal, like any failed command, reaches the
  program as `{ error, hint, type }`, which it may catch.
- A command the server does not have stops the program with the names nearest to it, not with "is not a function";
  `log(…)` is a global of the program, never a command.
- Bounded by `ProgramOptions`: the timeout (and the most a call may give), 32 MiB of memory, calls nested 256 deep, a
  million statements, a number of commands, the log and the size of the result — each stop with words of its own and a
  `type` (`timeout`, `programTooLarge`, `tooManyCalls`, `syntax`, `runtime`, `noSuchCommand`, `refused`, `cancelled`).
- The answer is the envelope of `Snail.Sdk.Mcp.Hosting`: `{"ok":true,"data":{"result":…,"log":[…],"calls":[…]}}`, and on
  a stop the same data beside `error`, `hint` and `type`, because the commands a program sent before it stopped stand.

The engine has no access to the host's types: a program reaches nothing but the commands. Jint is not annotated for
trimming, so a native server that references this package sets `IlcTreatWarningsAsErrors` to false: the native compiler
reports Jint's own bridge to .NET types, which a program never crosses.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

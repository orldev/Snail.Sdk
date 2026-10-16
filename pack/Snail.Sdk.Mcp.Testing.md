# Snail.Sdk.Mcp.Testing

Contract tests of a Snail MCP server, over stdio, against the build a client runs:

- `ServerProgram.StartAsync(assembly, variable)`: the server started the way a client starts it — the dll beside the
  tests, or the published program the variable names — with an empty home of its own (`ServerProgram.Home`, a
  `TestFolder`), deleted when the test run ends, and no settings file (`ServerProgram.NoSettingsFile`, which never exists, in its
  `SNAIL_MCP_<SHORT>_CONFIG`). A server that never answers `initialize` fails the test after two minutes with the
  program named, and its process is ended.
- `TestFolder.Create()`: a temporary folder for a test, named `s3l-test-<pid>-<start>-<random>` directly in the
  temporary folder — the test process's id and the second it started (Unix seconds, as the system reports it) — and
  deleted when the test process exits. A run that crashes leaves it behind, and its name proves it a leftover once no
  process with that id and start runs, so a cleaner removes it without guessing. Tests take their temporary folders
  from it rather than from `Directory.CreateTempSubdirectory()` or a random name under `Path.GetTempPath()`.
- `answer.Text()`: the text of a tool's answer.
- `ToolGuards.Problems(tools)`: every guard rule the listed tools break — snake-case unique names, descriptions in
  English for the tool and every parameter, the effect stated, every allowed value named.

```csharp
await using var client = await ServerProgram.StartAsync("Snail.MCP.Demo", "SNAIL_DEMO_SERVER");
var tools = await client.ListToolsAsync();

Assert.Empty(ToolGuards.Problems(tools.Select(tool => tool.ProtocolTool)));
```

A test project built on [Snail.Sdk.Mcp](https://www.nuget.org/packages/Snail.Sdk.Mcp) references it implicitly.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

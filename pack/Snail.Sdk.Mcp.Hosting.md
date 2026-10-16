# Snail.Sdk.Mcp.Hosting

What every Snail MCP server starts from:

- `McpHost.CreateBuilder(args)`: the empty host, with log lines on stderr (`UseStderr`) since stdout carries JSON-RPC,
  and the heap compacted once the server has heard no MCP message for five minutes (`AddIdleTrim`;
  `SNAIL_MCP_IDLE_TRIM` sets the seconds, `0` turns it off).
- `ToolJson.Options(MyJson.Default)`: the MCP SDK's JSON options with the server's source-generated types put first,
  so the tools work in a native build.
- `ServerSettings.Read(name)` and `configuration.AddServerSettings(name)`: the server's settings file — the one
  `SNAIL_MCP_<NAME>_CONFIG` names, and only that one, or else the first of `./.s3l-mcp-<name>.json`,
  `./s3l-mcp-<name>.json`, `~/.config/s3l-mcp-<name>/config.json` and `~/.s3l-mcp-<name>.json` that exists — then its
  `SNAIL_MCP_<NAME>_` variables, which win. Both host builders add them by themselves for a program Snail.Sdk.Mcp named.
- `EnvironmentSettings.Read(prefix, variables)`: settings from `PREFIX_SECTION__FIELD` environment variables.
- `ServerFolders.DataDirectory(name)` and `ServerFolders.ConfigDirectory(name)`: `~/.s3l-mcp-<name>` and
  `~/.config/s3l-mcp-<name>`.
- `ServerIdentity.Of(assembly)`: the name and version the server announces in `initialize`.
- `Invocation.Stable(path)`: the path of the running program that outlives an update of it.

A server built on [Snail.Sdk.Mcp](https://www.nuget.org/packages/Snail.Sdk.Mcp) references it implicitly.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

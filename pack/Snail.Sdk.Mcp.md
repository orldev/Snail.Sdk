# Snail.Sdk.Mcp

The MSBuild SDK of a Snail MCP server: [Snail.Sdk](https://www.nuget.org/packages/Snail.Sdk), plus a dotnet tool of the
McpServer kind described by three properties (native, transport, container), the server libraries referenced
implicitly ([Snail.Sdk.Mcp.Hosting](https://www.nuget.org/packages/Snail.Sdk.Mcp.Hosting),
[Snail.Sdk.Mcp.Http](https://www.nuget.org/packages/Snail.Sdk.Mcp.Http) for a shared server,
[Snail.Sdk.Mcp.Testing](https://www.nuget.org/packages/Snail.Sdk.Mcp.Testing) and
[Snail.Sdk.Mcp.JobClient](https://www.nuget.org/packages/Snail.Sdk.Mcp.JobClient) for its tests), the tool reference
generated from the server itself, and publishing of an installed copy under `~/.s3l-mcp/<name>/`.

A server and its test project both name it:

```xml
<Project Sdk="Snail.Sdk.Mcp">
```

```json
{ "msbuild-sdks": { "Snail.Sdk.Mcp": "0.1.0" } }
```

`dotnet new s3l-mcp` from [Snail.Sdk.Templates](https://www.nuget.org/packages/Snail.Sdk.Templates) starts a server
on it.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

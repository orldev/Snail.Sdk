# Snail.Sdk

The base MSBuild SDK of every Snail library and MCP server: language version, nullable, XML docs, warnings as errors,
code style in the build, the .NET analyzers by category with the agreed rules as warnings, the owner's own rules
([Snail.Sdk.Analyzers](https://www.nuget.org/packages/Snail.Sdk.Analyzers)), package audit, and deterministic,
source-linked builds. The kind of a project is read from its name: `*.Tests`, `*.Benchmarks`, `*.NativeProbe`.

A project names the SDK in its `Sdk` attribute and the version once, in `global.json`:

```xml
<Project Sdk="Snail.Sdk">
```

```json
{ "msbuild-sdks": { "Snail.Sdk": "0.1.0" } }
```

A library uses [Snail.Sdk.Library](https://www.nuget.org/packages/Snail.Sdk.Library) and a server
[Snail.Sdk.Mcp](https://www.nuget.org/packages/Snail.Sdk.Mcp); both build on this one.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

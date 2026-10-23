# Snail.Sdk

Snail.Sdk is the standard every Snail library and Snail MCP server is built by, delivered as packages: the MSBuild SDKs
that carry the build rules, the analyzers, the server libraries, the project templates, the shared GitHub workflows and
the conformance check that fails a repository's build when it drifts from the standard. A rule changed here reaches every
project with a version bump instead of a copy.

| Package | What it gives |
| --- | --- |
| `Snail.Sdk` | the build rules, the analyzers, the package audit, deterministic builds, the conformance check |
| `Snail.Sdk.Library` | a library: native compatibility, the public API written down and validated, symbols, the API reference |
| `Snail.Sdk.Mcp` | an MCP server: a dotnet tool package, native per platform, the tool reference, the local install |
| `Snail.Sdk.Mcp.Hosting`, `.Http`, `.Testing` | what a server runs on and is tested with |
| `Snail.Sdk.Analyzers` | the rules no .NET analyzer has |
| `Snail.Sdk.Templates` | `dotnet new s3l-lib`, `dotnet new s3l-mcp`, `dotnet new s3l-web` |

- [Getting started](getting-started.md): a new library, server or application, an existing repository brought onto Snail.Sdk, and running everything locally.
- [The standard](standard.md): what every repository looks like and why.
- [Guidelines](guidelines.md): how the C# is written.
- [Releasing](releasing.md) and [installing servers locally](installing.md).
- Reference: [MSBuild properties](properties.md), [workflows](workflows.md), [conformance](conformance.md),
  [analyzer rules](rules/index.md), and the API of [Snail.Sdk.Mcp.Hosting](reference/Snail.Sdk.Mcp.Hosting.md),
  [Snail.Sdk.Mcp.Http](reference/Snail.Sdk.Mcp.Http.md) and [Snail.Sdk.Mcp.Testing](reference/Snail.Sdk.Mcp.Testing.md).
- [Contributing](contributing.md): working on Snail.Sdk itself.

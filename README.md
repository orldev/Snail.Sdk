# Snail.Sdk

The standard every Snail library and Snail MCP server is built by, delivered as packages: the MSBuild SDKs that carry the
build rules, the analyzers, the server libraries, the project templates, the shared GitHub workflows and the conformance
check that fails a repository's build when it drifts. A rule changed here reaches every project with a version bump
instead of a copy. Documentation: <https://orldev.github.io/Snail.Sdk/>.

| Package | What it gives |
| --- | --- |
| `Snail.Sdk` | The MSBuild SDK of every Snail project: nullable, XML docs, warnings as errors, code style in the build, every .NET analyzer rule as advice and the agreed ones as warnings, `Snail.Sdk.Analyzers`, the package audit, deterministic source-linked builds, the project kinds (`*.Tests`, `*.Benchmarks`, `*.NativeProbe`, `Tool`), the conformance check. |
| `Snail.Sdk.Library` | `Snail.Sdk`, plus native compatibility unless `SnailNotNativeBecause` says why not, the public API kept in `PublicAPI.*.txt`, validation against the last release, symbol packages, the API reference and the site. |
| `Snail.Sdk.Mcp` | `Snail.Sdk`, plus a dotnet tool of the McpServer kind from three properties (`SnailMcpNative`, `SnailMcpTransport`, `SnailMcpContainer`), a native package per platform, the server libraries implicitly, the tool reference and the site, and the install into `~/.s3l-mcp` with `-p:SnailMcpInstall=true`. |
| `Snail.Sdk.Mcp.Hosting` | The empty host with log lines on stderr, the JSON options of the tools, settings from prefixed environment variables, the server's folders, its name and version, the stable path of the running program. |
| `Snail.Sdk.Mcp.Http` | One server for every session of the machine: the loopback web host (or, for a container image, the published port on every interface), Streamable HTTP with a session per client, the refusal of browser requests. Apart from Hosting so a native stdio server never references ASP.NET Core. |
| `Snail.Sdk.Mcp.Testing` | The server under test started the way a client starts it, with a home folder of its own, against the build or a published native program; `ToolGuards`. |
| `Snail.Sdk.Analyzers` | The rules no .NET analyzer has, S3L0001–S3L0010, with code fixes where the fix is mechanical. |
| `Snail.Sdk.Templates` | `dotnet new s3l-lib`, `dotnet new s3l-mcp` and `dotnet new s3l-web`: a repository that builds, tests and conforms before a line is changed. |

A project names its level in its `Sdk` attribute and the version once, in `global.json`:

```xml
<Project Sdk="Snail.Sdk.Mcp">
```

```json
"msbuild-sdks": { "Snail.Sdk": "0.1.0", "Snail.Sdk.Mcp": "0.1.0" }
```

A new project starts from the templates:

```bash
dotnet new install Snail.Sdk.Templates
dotnet new s3l-lib -n Snail.Toolkit.Foo
dotnet new s3l-mcp -n Snail.MCP.Foo
dotnet new s3l-web -n Vertical.Foo       # an ASP.NET application
```

## Documentation

- [Getting started](docs/getting-started.md) — a new library or server, before and after Snail.Sdk is on NuGet, and
  running everything locally.
- [The standard](docs/standard.md) — what every repository looks like, how it is built, tested, documented and
  released, and why.
- [Guidelines](docs/guidelines.md) — how the C# is written.
- [Releasing](docs/releasing.md) — libraries, servers, images, Snail.Sdk itself and the `v1` tag of the workflows.
- [Installing servers locally](docs/installing.md) — `~/.s3l-mcp`, registration in Claude Code, launchd.
- Reference — [MSBuild properties](docs/properties.md), [workflows](docs/workflows.md),
  [conformance](docs/conformance.md), [analyzer rules](docs/rules/index.md).
- [Contributing](docs/contributing.md) — working on Snail.Sdk itself.

## Building

```bash
dotnet build Snail.Sdk.slnx -c Release
dotnet test Snail.Sdk.slnx -c Release
```

The tests pack every package into a temporary feed and build real projects on it; what they need is in
[contributing](docs/contributing.md).

## License

MIT, see [LICENSE](LICENSE).

# Snail.Sdk.Mcp.Http

One Snail MCP server for every session of the machine, over Streamable HTTP:

- `McpHttpHost.CreateBuilder(args, url)`: the empty web host on a loopback address, given to Kestrel itself, so a
  configuration the server registers cannot move it; any other address is refused. `CreateBuilder(args, endpoint)`
  builds the host an `McpEndpoint` names, and `CreateBuilder(args, endpoint, settings)` builds it over the settings the
  program read already.
- `McpHttpHost.CreateContainerBuilder(args, port)`: the same host for a server in a container image, on every interface,
  since a container's published port does not reach its loopback.
- `WithLocalHttpTransport()`: Streamable HTTP with a session for every client that opens with `initialize`.
- `MapLocalMcp()`: the endpoint at `/mcp`, behind the refusal of every request that carries an `Origin`, so no web
  page can reach the server. `MapHealth()`: `/healthz`, 200 with no body; `MapHealth(path, isHealthy)` answers 503
  while the server cannot serve.
- `WithSessionAddress()`: the session's address read from the query of each request — `WithLocalHttpTransport` adds it
  by itself — and `RefuseSessionsItCannotServe(volumes)`: 400 at the door for a session whose root this machine does
  not have or the volumes do not hold.
- `JobOwners.Session`: a job owned by the MCP session that started it, `.WithJobs("<short>", JobOwners.Session)`.

It is kept apart from [Snail.Sdk.Mcp.Hosting](https://www.nuget.org/packages/Snail.Sdk.Mcp.Hosting) so that a stdio
server never references ASP.NET Core. A server built on [Snail.Sdk.Mcp](https://www.nuget.org/packages/Snail.Sdk.Mcp)
with an HTTP transport references it implicitly.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

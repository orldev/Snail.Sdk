# Snail.Sdk.Mcp.Http

One Snail MCP server for every session of the machine, over Streamable HTTP:

- `McpHttpHost.CreateBuilder(args, url)`: the empty web host on a loopback address; any other address is refused.
- `McpHttpHost.CreateContainerBuilder(args, port)`: the same host for a server in a container image, on every interface,
  since a container's published port does not reach its loopback.
- `WithLocalHttpTransport()`: Streamable HTTP with a session for every client that opens with `initialize`.
- `MapLocalMcp()`: the endpoint at `/mcp`, behind the refusal of every request that carries an `Origin`, so no web
  page can reach the server.

It is kept apart from [Snail.Sdk.Mcp.Hosting](https://www.nuget.org/packages/Snail.Sdk.Mcp.Hosting) so that a stdio
server never references ASP.NET Core. A server built on [Snail.Sdk.Mcp](https://www.nuget.org/packages/Snail.Sdk.Mcp)
with an HTTP transport references it implicitly.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

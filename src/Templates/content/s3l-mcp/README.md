# Snail.MCP.Template

TEMPLATE_DESCRIPTION

Its tools: [docs/reference/tools](docs/reference/tools/index.md), written from the server itself. Built on
[Snail.Sdk](https://github.com/orldev/Snail.Sdk): the rules, the dotnet tool package, publishing into `~/.s3l-mcp`, the
shared workflows.
<!--#if (jobs) -->

A long operation runs as a job: `tmplshort_work` starts one and answers within the `wait` the client allows, or with
the job's resource `job://tmplshort/work/<id>` to read; a client that can wait is told when it ends
([Snail.Sdk.Mcp.JobClient](https://www.nuget.org/packages/Snail.Sdk.Mcp.JobClient)).
<!--#endif -->

```bash
dotnet publish src -c Release -r osx-arm64 -p:SnailMcpInstall=true    # ~/.s3l-mcp/Snail.MCP.Template/current
```

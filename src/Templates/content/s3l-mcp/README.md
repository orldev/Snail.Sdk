# Snail.MCP.Template

TEMPLATE_DESCRIPTION

Its tools: [docs/reference/tools](docs/reference/tools/index.md), written from the server itself. Built on
[Snail.Sdk](https://github.com/orldev/Snail.Sdk): the rules, the dotnet tool package, publishing into `~/.s3l-mcp`, the
shared workflows.

Every tool answers with the envelope of `Snail.Sdk.Mcp.Hosting.Answers.ToolAnswer`, `{"ok":true,"data":…}` or
`{"ok":false,"error":…,"hint":…,"type":…}` flagged `isError`; a tool that throws is answered the same way (`ExplainFailures`).
<!--#if (UsesHttp) -->

Over HTTP one server serves every session at `http://127.0.0.1:9300/mcp` (`SNAIL_MCP_TMPLSHORT_URL`; every interface
inside a container), and `GET /healthz` answers 200. A platform that gives a role some of the tools names them in the
address, `/mcp?tool=tmplshort_diagnose`: the others are left out of the list and refused.
<!--#endif -->
<!--#if (jobs) -->

A long operation runs as a job: `tmplshort_work` starts one and answers within the `wait` the client allows, or with
the job's resource `job://tmplshort/work/<id>` to read; a client that can wait is told when it ends
([Snail.Sdk.Mcp.JobClient](https://www.nuget.org/packages/Snail.Sdk.Mcp.JobClient)).
<!--#endif -->

```bash
dotnet publish src -c Release -r osx-arm64 -p:SnailMcpInstall=true    # ~/.s3l-mcp/Snail.MCP.Template/current
```

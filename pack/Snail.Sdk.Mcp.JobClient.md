# Snail.Sdk.Mcp.JobClient

The client side of a Snail MCP server's jobs. A server built on
[Snail.Sdk.Mcp.Hosting](https://www.nuget.org/packages/Snail.Sdk.Mcp.Hosting) with `WithJobs` runs a long operation —
a render, an index pass, a test run — as a job: a tool starts it and answers within the `wait` the client allows, or at
once with the job's resource, `job://<server>/<kind>/<id>`, whose text is the job's report. This library waits for that
job by being told, not by asking:

- `client.CallJobAsync(tool, arguments)`: calls the tool with no wait and awaits the job it answers with.
- `client.AwaitJobAsync(uri)`: the final report of a job — subscribed to its resource, woken by every change, the
  resource read again when nothing was told within `JobAwaitOptions.Fallback` (half a minute), so a lost notification
  costs a wait, never a hang.
- `client.WatchJobAsync(uri)`: every report as it changes, the final one last.
- `client.ReadJobAsync(uri)`: the report as the resource shows it now.

```csharp
await using var client = await McpClient.CreateAsync(transport);

var report = await client.CallJobAsync("blender_render_job", new Dictionary<string, object?> { ["outputPath"] = "/renders/shot_####" });
```

The subscription takes the form the protocol revision allows: `resources/subscribe` up to 2025-11-25,
`subscriptions/listen` from 2026-07-28 — the MCP SDK's client sends the first and knows nothing of the second, which
is sent here. A model in the loop sees one call and its result, and spends no turn on waiting.

- Source and issues: [https://github.com/orldev/Snail.Sdk](https://github.com/orldev/Snail.Sdk)
- The standard: [orldev.github.io/Snail.Sdk](https://orldev.github.io/Snail.Sdk/)

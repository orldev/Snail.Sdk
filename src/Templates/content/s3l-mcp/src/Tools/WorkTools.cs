using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.Template.Tools;

/// <summary>A long operation as a job: the sample a server with work that outlasts a call starts from.</summary>
/// <remarks>
/// The tool starts the work and answers within the wait the client allows — the result when the work ends in time,
/// the running job otherwise, as the resource <c>job://tmplshort/work/&lt;id&gt;</c> whose text is the job's report. A
/// client that can wait (an orchestrator through <c>Snail.Sdk.Mcp.JobClient</c>, a test) subscribes to that resource
/// and is told when the job ends; a model reads the resource when it wants. No status tool: the resource is the status.
/// </remarks>
[McpServerToolType]
public sealed class WorkTools(Jobs jobs)
{
    /// <summary>Counts to a number, one step a tenth of a second, as a job.</summary>
    [McpServerTool(Name = "tmplshort_work", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description("Count to a number, a step every tenth of a second, as a job: answers with the count when it ends within the wait, or with the running job to read as a resource.")]
    public async Task<JobReport> WorkAsync(
        [Description("The number to count to, 1 to 600.")] int upTo,
        [Description("Seconds to wait for the count before answering with the job in flight; 0 answers at once.")] int wait = 60,
        CancellationToken cancellationToken = default)
    {
        var steps = Math.Clamp(upTo, 1, 600);
        var job = jobs.Start("work", async (progress, token) =>
        {
            for (var step = 1; step <= steps; step++)
            {
                await Task.Delay(100, token);
                progress.Report(new JobProgress(step, steps, $"counted to {step}"));
            }

            return new Counted(steps);
        }, WorkJson.Default.Counted);

        return await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);
    }
}

/// <summary>What a count ends with.</summary>
/// <param name="Count">The number counted to.</param>
public sealed record Counted(int Count);

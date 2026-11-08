using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.MCP.Template.Tools;

/// <summary>A long operation as a job: the sample a server with work that outlasts a call starts from.</summary>
/// <remarks>
/// The tool starts the work and answers within the wait the client allows — the result when the work ends in time,
/// the running job otherwise, as the resource <c>job://tmplshort/work/&lt;id&gt;</c> whose text is the job's report. The
/// answer is the envelope every answer is, with the report as its data and, alone, as its structured content
/// (JobAnswer.Envelope). A client that can wait (an orchestrator through <c>Snail.Sdk.Mcp.JobClient</c>, a test) finds
/// the report there, subscribes to the resource and is told when the job ends; a model reads the resource when it
/// wants. No status tool: the resource is the status.
/// </remarks>
[McpServerToolType]
public sealed class WorkTools(Jobs jobs)
{
    /// <summary>The most a count goes to: a minute of steps a tenth of a second each.</summary>
    public const int MostSteps = 600;

    /// <summary>Counts to a number, one step a tenth of a second, as a job.</summary>
    /// <remarks>A number out of range is refused in the envelope, never counted to the nearest bound: a clamp did what was not asked and said nothing.</remarks>
    [McpServerTool(Name = "tmplshort_work", ReadOnly = false, Destructive = false, OpenWorld = false)]
    [Description(ToolDescriptions.Work)]
    public async Task<CallToolResult> WorkAsync(
        [Description(ToolDescriptions.WorkUpTo)] int upTo,
        [Description(ToolDescriptions.WorkWait)] int wait = 60,
        CancellationToken cancellationToken = default)
    {
        if (upTo is < 1 or > MostSteps)
            return ToolAnswer.Failed($"tmplshort_work counts to a number from 1 to {MostSteps}, not {upTo}", $"Send upTo from 1 to {MostSteps}.", "arguments");

        Job job = jobs.Start("work", (progress, token) => CountAsync(upTo, progress, token), WorkJson.Default.Counted);
        JobReport report = await job.ForAsync(TimeSpan.FromSeconds(wait), cancellationToken);

        return JobAnswer.Envelope(report);
    }

    private static async Task<Counted> CountAsync(int upTo, IProgress<JobProgress> progress, CancellationToken cancellationToken)
    {
        for (int step = 1; step <= upTo; step++)
        {
            await Task.Delay(100, cancellationToken);
            progress.Report(new JobProgress(step, upTo, $"counted to {step}"));
        }

        return new Counted(upTo);
    }
}

/// <summary>What a count ends with.</summary>
/// <param name="Count">The number counted to.</param>
public sealed record Counted(int Count);

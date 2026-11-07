using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>A job as the answer of a tool that speaks in words of its own: its text, with the job's report beside it.</summary>
/// <remarks>
/// A tool that returns <see cref="JobReport"/> answers with the report as both its text and its structured content. A
/// server whose tools answer in a shape of their own — an <c>ok</c> flag with <c>data</c>, a result the model reads as it
/// always has — wants its text kept, and a client of <c>Snail.Sdk.Mcp.JobClient</c> still has to find the report: it reads
/// the structured content. Snail.MCP.E2E built this by hand; a tool now writes its text and hands the rest here.
/// </remarks>
public static class JobAnswer
{
    /// <summary>The tool result <paramref name="said"/> makes of the report, with the report itself as its structured content.</summary>
    /// <param name="report">The job as it stands: ended within the wait, or still running.</param>
    /// <param name="said">What the model reads: the tool's own answer once the job ended, or where to read the job while it runs.</param>
    public static CallToolResult Of(JobReport report, Func<JobReport, CallToolResult> said)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(said);

        var answer = said(report);
        answer.StructuredContent = JsonSerializer.SerializeToElement(report, JobJson.Default.JobReport);

        return answer;
    }

    /// <summary>The job as the envelope every answer is: the report as the data of the text, and as the structured content.</summary>
    /// <param name="report">The job as it stands: ended within the wait, or still running.</param>
    /// <param name="beside">Fields the tool adds to the data of the text — a note, where to look next; a field of the report's own name replaces it there.</param>
    /// <remarks>
    /// <para>
    /// The structured content stays the report alone, whatever goes beside it, since that is what a client of
    /// <c>Snail.Sdk.Mcp.JobClient</c> reads. Snail.MCP.Security, BuildBridge, AfterEffects and Blender each wrote this by hand.
    /// </para>
    /// <para>
    /// A job that failed or was cancelled within the wait answers as a failure — <c>ok</c> false, its message as the
    /// error, <c>failed</c> or <c>cancelled</c> as the type, the report still the data — and flagged an error. It answered
    /// <c>ok</c> true at first, and a model reads that as the work having been done (the s3l-mcp template, 0.5.0). Unlike
    /// other failures it keeps the report as its structured content: a job's tool has no output schema to break, and
    /// the job client finds the report nowhere else. A running job is no failure: it answers as a success with its URI.
    /// </para>
    /// </remarks>
    public static CallToolResult Envelope(JobReport report, JsonObject? beside = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var data = JsonSerializer.SerializeToNode(report, JobJson.Default.JobReport)!.AsObject();

        foreach (var (name, value) in beside ?? [])
            data[name] = value?.DeepClone();

        var answer = report.State switch
        {
            JobState.Failed => ToolAnswer.Failed(report.Message ?? JobFailed, type: "failed", data: data),
            JobState.Cancelled => ToolAnswer.Failed(JobCancelled, type: "cancelled", data: data),
            _ => ToolAnswer.Ok(data, AnswerShape.None)
        };

        answer.StructuredContent = JsonSerializer.SerializeToElement(report, JobJson.Default.JobReport);

        return answer;
    }

    private const string JobFailed = "The job failed.";

    private const string JobCancelled = "The job was cancelled before it ended.";
}

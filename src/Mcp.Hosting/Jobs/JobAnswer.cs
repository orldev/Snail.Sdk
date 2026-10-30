using System.Text.Json;
using ModelContextProtocol.Protocol;

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
}

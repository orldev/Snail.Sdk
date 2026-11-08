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
    /// <remarks>
    /// A job that failed or was cancelled is flagged an error whatever <paramref name="said"/> flagged, as
    /// <see cref="Envelope"/> and <see cref="Result"/> flag it: a client that never reads the text still learns the work
    /// was not done (Snail.MCP.CodeContext).
    /// </remarks>
    public static CallToolResult Of(JobReport report, Func<JobReport, CallToolResult> said)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(said);

        var answer = said(report);
        answer.StructuredContent = JsonSerializer.SerializeToElement(report, JobJson.Default.JobReport);

        if (report.State is JobState.Failed or JobState.Cancelled)
            answer.IsError = true;

        return answer;
    }

    /// <summary>The job as the envelope every answer is: the report as the data of the text, and as the structured content.</summary>
    /// <param name="report">The job as it stands: ended within the wait, or still running.</param>
    /// <param name="beside">Fields the tool adds to the data of the text — a note, where to look next; a field of the report's own name replaces it there.</param>
    /// <param name="hint">What to do about a job that failed or was cancelled, when its report carries no hint of its own: start it again, read its log.</param>
    /// <param name="options">The server's words for a job that failed or was cancelled; the SDK's when null.</param>
    /// <remarks>
    /// <para>
    /// The structured content stays the report alone, whatever goes beside it, since that is what a client of
    /// <c>Snail.Sdk.Mcp.JobClient</c> reads. Snail.MCP.Security, BuildBridge, AfterEffects and Blender each wrote this by hand.
    /// </para>
    /// <para>
    /// A job that failed or was cancelled within the wait answers as a failure — <c>ok</c> false, its message as the
    /// error, the hint and type of the answer its work failed with (<c>internal</c> for one its work threw, <c>failed</c>
    /// for an answer that named none, <c>cancelled</c> for a cancelled one), the report still the data — and flagged an
    /// error. It answered <c>ok</c> true at first, and a model
    /// reads that as the work having been done (the s3l-mcp template, 0.5.0). Unlike other failures it keeps the report as
    /// its structured content: a job's tool has no output schema to break, and the job client finds the report nowhere
    /// else. A running job is no failure: it answers as a success with its URI, as <see cref="Pending"/> writes it.
    /// </para>
    /// <para>
    /// The words of a failure are the server's where <paramref name="options"/> give them: a hint chosen for the report —
    /// start it again after a cancellation, read the log after a failure — and the error of a cancelled job. One hint for
    /// both, and an error no server could change, had each server work its hint out before every call (Snail.MCP.Security,
    /// AfterEffects). The report's own hint wins, then <paramref name="hint"/>, then the options'.
    /// </para>
    /// </remarks>
    public static CallToolResult Envelope(JobReport report, JsonObject? beside = null, string? hint = null, JobAnswerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.IsRunning)
            return Pending(report, beside);

        return Told(report, ReportWith(report, beside), hint, options);
    }

    /// <summary>The job still running, as the envelope: a success whose data is the report — its URI, its state, how far it has come — with the fields of <paramref name="beside"/>, and the report as the structured content.</summary>
    /// <param name="report">The job as it stands, still running.</param>
    /// <param name="beside">Fields the tool adds to the data of the text — a note, the file it will write; a field of the report's own name replaces it there.</param>
    /// <param name="hint">What to do next: while the job runs, the success's hint — where to read it, when to look again; once it ended, what to do about a failure, as <see cref="Envelope"/> takes it.</param>
    /// <param name="options">The server's words for a job that failed or was cancelled, used only when the report has already ended.</param>
    /// <remarks>
    /// <para>
    /// The answer <see cref="Envelope"/> and <see cref="Result"/> give a running job, to the byte — they write it through
    /// here, with no hint — for a tool that answers a job in flight in a way of its own and its result otherwise, rather
    /// than build the report, the fields beside it and the structured content by hand.
    /// </para>
    /// <para>
    /// A report that has already ended — the work finished between the wait and the answer — is no mistake of the caller's,
    /// and is answered as <see cref="Envelope"/> answers it, with <paramref name="beside"/>, <paramref name="hint"/> and
    /// <paramref name="options"/>: a failure flagged as one, never a success that says it runs.
    /// </para>
    /// </remarks>
    public static CallToolResult Pending(JobReport report, JsonObject? beside = null, string? hint = null, JobAnswerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.IsRunning)
            return Envelope(report, beside, hint, options);

        var answer = ToolAnswer.Ok(ReportWith(report, beside), AnswerShape.None, hint: hint);
        answer.StructuredContent = JsonSerializer.SerializeToElement(report, JobJson.Default.JobReport);

        return answer;
    }

    /// <summary>
    /// The job as the answer its tool gives when the tool's answer is the work's result: once it succeeded, the result
    /// itself as the data; once it failed, the failure its work answered; while it runs, the report with
    /// <paramref name="running"/> beside it. The report is the structured content throughout.
    /// </summary>
    /// <param name="report">The job as it stands: ended within the wait, or still running.</param>
    /// <param name="running">Fields added to the report while the job runs — a note, the file it will write; a field of the report's own name replaces it there.</param>
    /// <param name="hint">What to do about a job that failed or was cancelled, when its report carries no hint of its own.</param>
    /// <param name="options">The server's words for a job that failed or was cancelled, as <see cref="Envelope"/> takes them; the SDK's when null.</param>
    /// <remarks>
    /// <para>
    /// For a tool whose answer was its work's before the work became a job: a raise answers the environment, an export
    /// the file it wrote, whether the client waited or read the job later. With the work started as an
    /// <c>Answer&lt;T&gt;</c> (<see cref="Jobs.Start{T}(string, string?, Func{IProgress{JobProgress}, CancellationToken, Task{Answer{T}}}, System.Text.Json.Serialization.Metadata.JsonTypeInfo{T})"/>),
    /// a failure ends the job failed with its hint and type, and is answered as the failure it is — <c>ok</c> false and
    /// flagged an error, never a success whose data says <c>ok</c> false. Snail.MCP.E2E and Snail.MCP.Connector kept the
    /// envelope as the job's result and parsed it back to answer it.
    /// </para>
    /// <para>
    /// A failed or cancelled job answers as <see cref="Envelope"/> does, the report as its data. A job that succeeded with
    /// no result does too, since there is nothing else to answer with.
    /// </para>
    /// </remarks>
    public static CallToolResult Result(JobReport report, JsonObject? running = null, string? hint = null, JobAnswerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.State == JobState.Succeeded && report.Result is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } result)
        {
            var answer = ToolAnswer.Ok(result, ToolAnswer.ElementInfo, AnswerShape.None);
            answer.StructuredContent = JsonSerializer.SerializeToElement(report, JobJson.Default.JobReport);

            return answer;
        }

        if (report.IsRunning)
            return Pending(report, running);

        return Told(report, ReportWith(report, null), hint, options);
    }

    private static JsonObject ReportWith(JobReport report, JsonObject? beside)
    {
        var data = JsonSerializer.SerializeToNode(report, JobJson.Default.JobReport)!.AsObject();

        foreach (var (name, value) in beside ?? [])
            data[name] = value?.DeepClone();

        return data;
    }

    private static CallToolResult Told(JobReport report, JsonObject data, string? hint, JobAnswerOptions? options)
    {
        var answer = report.State switch
        {
            JobState.Failed => ToolAnswer.Failed(report.Message ?? JobFailed, report.Hint ?? hint ?? options?.Hint?.Invoke(report), report.Type ?? Failed, data),
            JobState.Cancelled => ToolAnswer.Failed(options?.Cancelled ?? JobCancelled, report.Hint ?? hint ?? options?.Hint?.Invoke(report), Cancelled, data),
            _ => ToolAnswer.Ok(data, AnswerShape.None)
        };

        answer.StructuredContent = JsonSerializer.SerializeToElement(report, JobJson.Default.JobReport);

        return answer;
    }

    private const string Failed = "failed";

    private const string Cancelled = "cancelled";

    private const string JobFailed = "The job failed.";

    /// <summary>The error of a cancelled job unless the server words its own.</summary>
    internal const string JobCancelled = "The job was cancelled before it ended.";
}

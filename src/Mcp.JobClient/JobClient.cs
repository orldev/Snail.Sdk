using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Hosting.Jobs;

namespace Snail.Sdk.Mcp.JobClient;

/// <summary>Waits for a job of a Snail MCP server by being told, rather than by asking.</summary>
/// <remarks>
/// A job is what a tool with a <c>wait</c> parameter answers with when its work outlasts the wait: a resource at
/// <c>job://&lt;server&gt;/&lt;kind&gt;/&lt;id&gt;</c> whose text is the job's report. A client of this library calls
/// such a tool with no wait at all, subscribes to the resource, and is woken by the server when the job changes; a
/// model in the loop sees one call and its result, and spends no turn on waiting.
/// </remarks>
public static class JobClient
{
    /// <summary>The name of the parameter a job tool takes its wait in, seconds; zero answers with the job at once.</summary>
    public const string WaitParameter = "wait";

    extension(McpClient client)
    {
        /// <summary>The job's report as its resource shows it now.</summary>
        /// <exception cref="InvalidOperationException">The resource is not a job's: its text is not a report.</exception>
        public async Task<JobReport> ReadJobAsync(string uri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(uri);

            var read = await client.ReadResourceAsync(uri, cancellationToken: cancellationToken).ConfigureAwait(false);
            var text = read.Contents.OfType<TextResourceContents>().FirstOrDefault()?.Text;

            return (text is null ? null : JobReport.FromJson(text)) ?? throw new InvalidOperationException($"'{uri}' is not a job: its resource carries no report.");
        }

        /// <summary>
        /// The final report of a job: subscribed to its resource, woken by each change, the resource read again when no
        /// change was told within the fallback. Cancelling the token ends the wait, not the job.
        /// </summary>
        public async Task<JobReport> AwaitJobAsync(string uri, JobAwaitOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(uri);

            await foreach (var report in client.WatchJobAsync(uri, options, cancellationToken).ConfigureAwait(false))
            {
                if (!report.IsRunning)
                    return report;
            }

            throw new OperationCanceledException(cancellationToken);
        }

        /// <summary>Every report of a job as it changes, the final one last; a job already ended yields its report once.</summary>
        public async IAsyncEnumerable<JobReport> WatchJobAsync(string uri, JobAwaitOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(uri);

            var settings = options ?? new JobAwaitOptions();
            var changes = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions { SingleReader = true });
            await using var watch = await JobWatch.OpenAsync(client, uri, () => changes.Writer.TryWrite(true), cancellationToken).ConfigureAwait(false);
            var last = await client.ReadJobAsync(uri, cancellationToken).ConfigureAwait(false);

            settings.Progress?.Report(last);
            yield return last;

            while (last.IsRunning)
            {
                await ChangedOrFallbackAsync(changes.Reader, settings.Fallback, cancellationToken).ConfigureAwait(false);
                var read = await client.ReadJobAsync(uri, cancellationToken).ConfigureAwait(false);

                if (read == last)
                    continue;

                last = read;
                settings.Progress?.Report(last);
                yield return last;
            }
        }

        /// <summary>
        /// Calls a tool that starts a job, with no wait, and awaits the job it answers with; a tool that answers with a
        /// report already ended returns it as it is.
        /// </summary>
        /// <exception cref="InvalidOperationException">The tool's answer is not a job's report.</exception>
        public async Task<JobReport> CallJobAsync(string tool, IReadOnlyDictionary<string, object?>? arguments = null, JobAwaitOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(tool);

            var withoutWait = new Dictionary<string, object?>(arguments ?? new Dictionary<string, object?>(), StringComparer.Ordinal) { [WaitParameter] = 0 };
            var answer = await client.CallToolAsync(tool, withoutWait, cancellationToken: cancellationToken).ConfigureAwait(false);
            var report = ReportOf(answer) ?? throw new InvalidOperationException($"'{tool}' answered with no job: {Text(answer)}");

            return report.IsRunning ? await client.AwaitJobAsync(report.Uri, options, cancellationToken).ConfigureAwait(false) : report;
        }
    }

    /// <summary>The job's report in a tool's answer: its structured content, or its text.</summary>
    private static JobReport? ReportOf(CallToolResult answer)
    {
        if (answer.StructuredContent is { } structured && JobReport.FromJson(structured.GetRawText()) is { } fromStructure)
            return fromStructure;

        return Text(answer) is { } text ? JobReport.FromJson(text) : null;
    }

    private static string? Text(CallToolResult answer) =>
        answer.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    /// <summary>Waits for a change, or for the fallback to pass, whichever first; cancellation ends the wait.</summary>
    private static async Task ChangedOrFallbackAsync(ChannelReader<bool> changes, TimeSpan fallback, CancellationToken cancellationToken)
    {
        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(fallback);

        try
        {
            await changes.ReadAsync(window.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }
}

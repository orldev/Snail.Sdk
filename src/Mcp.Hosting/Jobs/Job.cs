using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>A job in flight or ended: what a tool answers with, or waits on, and what its resource shows.</summary>
/// <remarks>
/// The work runs on its own task from the moment the job is made; the job outlives the tool call that started it. Every
/// change of state or progress is one report, handed to the watchers <see cref="Jobs"/> keeps, so a subscriber is told
/// rather than asking.
/// </remarks>
public sealed class Job
{
    private readonly CancellationTokenSource _cancelling = new();
    private readonly TaskCompletionSource<JobReport> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<JobReport> _changed;
    private readonly Lock _gate = new();
    private JobReport _report;

    internal Job(string uri, DateTimeOffset startedAt, Action<JobReport> changed)
    {
        _changed = changed;
        _report = new JobReport(uri, JobState.Running, startedAt, null, null, null, null);
        Uri = uri;
    }

    /// <summary>The resource the job is read and watched at.</summary>
    public string Uri { get; }

    /// <summary>The job as it stands now.</summary>
    public JobReport Report
    {
        get
        {
            lock (_gate)
                return _report;
        }
    }

    /// <summary>The final report: succeeded, failed or cancelled. It never throws.</summary>
    public Task<JobReport> Completion => _ended.Task;

    /// <summary>Stops the work; the report ends cancelled once the work has stopped.</summary>
    public void Cancel() => _cancelling.Cancel();

    /// <summary>The report when the job ends within <paramref name="wait"/>, or the running report with its URI when it does not.</summary>
    /// <remarks>
    /// How a tool answers every client with one call: a client that can wait gets the result, one that cannot gets the
    /// job to read later. Zero waits not at all. The token ends the wait, not the job.
    /// </remarks>
    public async Task<JobReport> ForAsync(TimeSpan wait, CancellationToken cancellationToken = default)
    {
        if (wait <= TimeSpan.Zero || _ended.Task.IsCompleted)
            return Report;

        var ended = await Task.WhenAny(_ended.Task, Task.Delay(wait, cancellationToken)).ConfigureAwait(false);

        if (ended != _ended.Task)
            cancellationToken.ThrowIfCancellationRequested();

        return Report;
    }

    internal async Task RunAsync<TResult>(Func<IProgress<JobProgress>, CancellationToken, Task<TResult>> work, JsonTypeInfo<TResult> resultType, CancellationToken stopping, TimeProvider clock)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(_cancelling.Token, stopping);
        var progress = new Progress<JobProgress>(step => Change(report => report with { Progress = step.Share ?? report.Progress, Message = step.What ?? report.Message }));

        try
        {
            var result = await work(progress, either.Token).ConfigureAwait(false);

            End(JobState.Succeeded, null, JsonSerializer.SerializeToElement(result, resultType), clock);
        }
        catch (OperationCanceledException) when (either.IsCancellationRequested)
        {
            End(JobState.Cancelled, "cancelled", null, clock);
        }
        catch (Exception failure)
        {
            End(JobState.Failed, failure.Message, null, clock);
        }
    }

    private void End(JobState state, string? message, JsonElement? result, TimeProvider clock)
    {
        Change(report => report with { State = state, EndedAt = clock.GetUtcNow(), Progress = state == JobState.Succeeded ? 1 : report.Progress, Message = message ?? report.Message, Result = result });
        _ended.TrySetResult(Report);
        _cancelling.Dispose();
    }

    private void Change(Func<JobReport, JobReport> change)
    {
        JobReport changed;

        lock (_gate)
        {
            if (!_report.IsRunning)
                return;

            _report = change(_report);
            changed = _report;
        }

        _changed(changed);
    }
}

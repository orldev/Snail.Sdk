using System.Text.Json;
using Snail.Sdk.Mcp.Hosting.Answers;

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

    internal Job(string uri, string? owner, DateTimeOffset startedAt, Action<JobReport> changed)
    {
        _changed = changed;
        _report = new JobReport(uri, JobState.Running, startedAt, null, null, null, null);
        Uri = uri;
        Owner = owner;
    }

    /// <summary>The resource the job is read and watched at.</summary>
    public string Uri { get; }

    /// <summary>Who the job belongs to, or null when it is anyone's who holds its URI.</summary>
    public string? Owner { get; }

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

    /// <summary>Runs the work to its end; <paramref name="kept"/> is told of the end before <see cref="Completion"/> is, so whoever awaits the job finds the jobs kept as they will stand.</summary>
    internal async Task RunAsync(Func<IProgress<JobProgress>, CancellationToken, Task<JobEnding>> work, CancellationToken stopping, TimeProvider clock, Action<Job> kept)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(_cancelling.Token, stopping);
        var progress = new Steps(step => Change(report => report with { Progress = step.Share ?? report.Progress, Message = step.What ?? report.Message }));

        try
        {
            var ending = await work(progress, either.Token).ConfigureAwait(false);

            End(ending, clock, kept);
        }
        catch (OperationCanceledException) when (either.IsCancellationRequested)
        {
            End(new JobEnding(JobState.Cancelled, "cancelled"), clock, kept);
        }
        catch (ToolRefusalException refusal)
        {
            End(new JobEnding(JobState.Failed, refusal.Error, Jobs.DetailsOf(refusal.Details), refusal.Hint, refusal.Type), clock, kept);
        }
        catch (Exception failure)
        {
            End(new JobEnding(JobState.Failed, failure.Message), clock, kept);
        }
    }

    private void End(JobEnding ending, TimeProvider clock, Action<Job> kept)
    {
        Change(report => report with
        {
            State = ending.State,
            EndedAt = clock.GetUtcNow(),
            Progress = ending.State == JobState.Succeeded ? 1 : report.Progress,
            Message = ending.Message ?? report.Message,
            Result = ending.Result,
            Hint = ending.Hint,
            Type = ending.Type,
        });
        kept(this);
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

    /// <summary>Each step written into the report as the work reports it.</summary>
    /// <remarks>
    /// <see cref="Progress{T}"/> posts a step to be handled later, so a job asked right after a step stood without it: the
    /// first answer of a running job carried no step, and a step reported just before the end could land after it.
    /// </remarks>
    private sealed class Steps(Action<JobProgress> step) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => step(value);
    }
}

/// <summary>How the work of a job ended: its state, and the result or the failure the report takes.</summary>
internal readonly record struct JobEnding(JobState State, string? Message = null, JsonElement? Result = null, string? Hint = null, string? Type = null);

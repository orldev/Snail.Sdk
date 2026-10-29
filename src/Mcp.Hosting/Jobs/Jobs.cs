using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Hosting;

namespace Snail.Sdk.Mcp.Hosting.Jobs;

/// <summary>The jobs of a server: started by its tools, read as resources, watched by subscribers.</summary>
/// <remarks>
/// <para>
/// A long operation — a render, an index pass, a test run — is a job rather than a call the client waits on: the tool
/// starts it and answers with the job, within the wait the client allows or at once with the job's URI, and the job's
/// resource says where it stands from then on. A client that subscribes to the resource is told of every change
/// (<c>WithJobs</c> of <see cref="JobsRegistration"/>); one that cannot reads it when it wants.
/// </para>
/// <para>
/// Ended jobs are kept, the last <see cref="Kept"/> of them, so a client that was answered with a running job can read
/// its result after; a stdio server's memory ends with its process, so a job is read before the session ends. When
/// the host stops, every running job is cancelled, and the server exits with its client.
/// </para>
/// </remarks>
public sealed class Jobs
{
    /// <summary>How many ended jobs are kept to be read, newest first.</summary>
    public const int Kept = 20;

    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _ended = new();
    private readonly string _server;
    private readonly TimeProvider _clock;
    private readonly CancellationToken _stopping;
    private int _numbered;

    /// <summary>The jobs of the server named <paramref name="server"/> — its short name, the first part of every job's URI.</summary>
    public Jobs(string server, TimeProvider? clock = null, IHostApplicationLifetime? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        _server = server;
        _clock = clock ?? TimeProvider.System;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
    }

    /// <summary>Raised with every change of any job: a state, a progress step.</summary>
    public event Action<JobReport>? Changed;

    /// <summary>Every job still running.</summary>
    public IReadOnlyList<Job> InFlight => [.. _jobs.Values.Where(job => job.Report.IsRunning).OrderBy(job => job.Report.StartedAt)];

    /// <summary>The jobs ended last, newest first, up to <see cref="Kept"/>.</summary>
    public IReadOnlyList<Job> Recent => [.. _ended.Reverse().Select(uri => _jobs[uri])];

    /// <summary>The URI template every job of this server matches.</summary>
    public string UriTemplate => $"job://{_server}/{{kind}}/{{id}}";

    /// <summary>
    /// Starts work of a kind and answers with its job at once; the work runs on, reporting its steps through the
    /// progress it is given and stopping on the token it is given.
    /// </summary>
    /// <param name="kind">What the work is, one word a URI can carry: <c>render</c>, <c>index</c>.</param>
    /// <param name="work">The work; its result becomes the job's, serialized with <paramref name="resultType"/>.</param>
    /// <param name="resultType">The result's type information, from the server's generated context: a native build looks nothing up at run time.</param>
    public Job Start<TResult>(string kind, Func<IProgress<JobProgress>, CancellationToken, Task<TResult>> work, JsonTypeInfo<TResult> resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(resultType);

        var uri = $"job://{_server}/{kind}/{Interlocked.Increment(ref _numbered)}";
        var job = new Job(uri, _clock.GetUtcNow(), report => Changed?.Invoke(report));

        _jobs[uri] = job;
        _ = RunAsync(job, work, resultType);

        return job;
    }

    /// <summary>The job at a URI, or null when there is none: never started, or ended and forgotten.</summary>
    public Job? Find(string uri) => _jobs.GetValueOrDefault(uri);

    private async Task RunAsync<TResult>(Job job, Func<IProgress<JobProgress>, CancellationToken, Task<TResult>> work, JsonTypeInfo<TResult> resultType)
    {
        await Task.Yield();
        await job.RunAsync(work, resultType, _stopping, _clock).ConfigureAwait(false);

        _ended.Enqueue(job.Uri);

        while (_ended.Count > Kept && _ended.TryDequeue(out var forgotten))
            _jobs.TryRemove(forgotten, out _);
    }
}

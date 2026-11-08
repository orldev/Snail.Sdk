using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Hosting;
using Snail.Sdk.Mcp.Hosting.Answers;

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
/// A job's URI is its own random id, never a count: a client that holds the URI was given it. On a server many clients
/// share — one HTTP server for every task of a platform — a job may also belong to an owner, and is then read and watched
/// only by a client of that owner (<c>WithJobs</c> with an owner, <see cref="Find(string, string?)"/>). Numbered jobs let
/// one task read another's test output by counting.
/// </para>
/// <para>
/// Ended jobs are kept, the last <see cref="Kept"/> of each owner and <see cref="KeptInAll"/> of the server, so a client
/// that was answered with a running job can read its result after; a stdio server's memory ends with its process, so a
/// job is read before the session ends. Counted for the whole server, twenty ended jobs of other tasks forgot a task's
/// result before it was read (Snail.MCP.E2E, 2026-10-30). When the host stops, every running job is cancelled, and the
/// server exits with its client.
/// </para>
/// </remarks>
public sealed class Jobs
{
    /// <summary>How many ended jobs of one owner are kept to be read, newest first; jobs without an owner count as one owner's.</summary>
    public const int Kept = 20;

    /// <summary>How many ended jobs of every owner together are kept: past it the oldest is forgotten, whoever owns it.</summary>
    /// <remarks>Owners come and go — a task, a session — and the jobs of one that never came back would otherwise stay for ever.</remarks>
    public const int KeptInAll = 500;

    private readonly ConcurrentDictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly LinkedList<Job> _ended = new();
    private readonly Dictionary<string, Queue<LinkedListNode<Job>>> _endedOf = new(StringComparer.Ordinal);
    private readonly string _server;
    private readonly TimeProvider _clock;
    private readonly CancellationToken _stopping;

    /// <summary>The jobs of the server named <paramref name="server"/> — its short name, the first part of every job's URI.</summary>
    public Jobs(string server, TimeProvider? clock = null, IHostApplicationLifetime? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(server);

        _server = server;
        _clock = clock ?? TimeProvider.System;
        _stopping = lifetime?.ApplicationStopping ?? CancellationToken.None;
    }

    /// <summary>The services a work that threw is read with, as <c>ExplainFailures</c> reads a tool that threw; the defaults when null.</summary>
    internal IServiceProvider? Services { get; init; }

    /// <summary>Raised with every change of any job: a state, a progress step.</summary>
    public event Action<JobReport>? Changed;

    /// <summary>Every job still running.</summary>
    public IReadOnlyList<Job> InFlight => [.. _jobs.Values.Where(job => job.Report.IsRunning).OrderBy(job => job.Report.StartedAt)];

    /// <summary>The ended jobs still kept, newest first: up to <see cref="Kept"/> of each owner and <see cref="KeptInAll"/> in all.</summary>
    public IReadOnlyList<Job> Recent
    {
        get
        {
            lock (_gate)
                return [.. _ended.Reverse()];
        }
    }

    /// <summary>The URI template every job of this server matches.</summary>
    public string UriTemplate => $"job://{_server}/{{kind}}/{{id}}";

    /// <summary>
    /// Starts work of a kind and answers with its job at once; the work runs on, reporting its steps through the
    /// progress it is given and stopping on the token it is given.
    /// </summary>
    /// <param name="kind">What the work is, one word a URI can carry: <c>render</c>, <c>index</c>.</param>
    /// <param name="work">The work; its result becomes the job's, serialized with <paramref name="resultType"/>.</param>
    /// <param name="resultType">The result's type information, from the server's generated context: a native build looks nothing up at run time.</param>
    public Job Start<TResult>(string kind, Func<IProgress<JobProgress>, CancellationToken, Task<TResult>> work, JsonTypeInfo<TResult> resultType) =>
        Start(kind, null, work, resultType);

    /// <summary>
    /// Starts work of a kind for an owner and answers with its job at once: only a client the server names that owner
    /// reads or watches it. A null owner leaves it to whoever holds its URI.
    /// </summary>
    /// <param name="kind">What the work is, one word a URI can carry: <c>render</c>, <c>index</c>.</param>
    /// <param name="owner">Who the job belongs to, in the server's own terms — a session, a task, a user — as <c>WithJobs</c> names the owner of a request.</param>
    /// <param name="work">The work; its result becomes the job's, serialized with <paramref name="resultType"/>.</param>
    /// <param name="resultType">The result's type information, from the server's generated context.</param>
    public Job Start<TResult>(string kind, string? owner, Func<IProgress<JobProgress>, CancellationToken, Task<TResult>> work, JsonTypeInfo<TResult> resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(resultType);

        return Run(kind, owner, async (progress, token) => new JobEnding(JobState.Succeeded, Result: JsonSerializer.SerializeToElement(await work(progress, token).ConfigureAwait(false), resultType)));
    }

    /// <summary>
    /// Starts work that answers an <see cref="Answer{T}"/> and answers with its job at once: a success ends the job
    /// succeeded with the value as its result, a failure ends it failed with the answer's error, hint and type.
    /// </summary>
    /// <remarks>
    /// Without it a server either threw away the hint and type of a failure to end the job failed, or ended it succeeded
    /// with <c>{"ok":false,…}</c> as its result — a failure the job's resource, its client and its tool all read as work
    /// done (Snail.MCP.BuildBridge's builds over a dropped SSH, Snail.MCP.E2E, Snail.MCP.Connector).
    /// </remarks>
    /// <param name="kind">What the work is, one word a URI can carry: <c>render</c>, <c>index</c>.</param>
    /// <param name="work">The work; the value of its answer becomes the job's result, serialized with <paramref name="resultType"/>.</param>
    /// <param name="resultType">The value's type information, from the server's generated context.</param>
    public Job Start<T>(string kind, Func<IProgress<JobProgress>, CancellationToken, Task<Answer<T>>> work, JsonTypeInfo<T> resultType) =>
        Start(kind, null, work, resultType);

    /// <summary>
    /// Starts work that answers an <see cref="Answer{T}"/> for an owner and answers with its job at once: a success ends
    /// the job succeeded with the value as its result, a failure ends it failed with the answer's error, hint and type.
    /// </summary>
    /// <param name="kind">What the work is, one word a URI can carry: <c>render</c>, <c>index</c>.</param>
    /// <param name="owner">Who the job belongs to, in the server's own terms, as <c>WithJobs</c> names the owner of a request; null leaves it to whoever holds its URI.</param>
    /// <param name="work">The work; the value of its answer becomes the job's result, serialized with <paramref name="resultType"/>.</param>
    /// <param name="resultType">The value's type information, from the server's generated context.</param>
    public Job Start<T>(string kind, string? owner, Func<IProgress<JobProgress>, CancellationToken, Task<Answer<T>>> work, JsonTypeInfo<T> resultType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(resultType);

        return Run(kind, owner, async (progress, token) => Ending(await work(progress, token).ConfigureAwait(false), resultType));
    }

    /// <summary>The job at a URI, or null when there is none: never started, or ended and forgotten.</summary>
    public Job? Find(string uri) => _jobs.GetValueOrDefault(uri);

    /// <summary>The job at a URI as a client of <paramref name="owner"/> may see it: null when there is none, or when it belongs to another owner.</summary>
    /// <remarks>A job without an owner is anyone's who holds its URI; a client without an owner sees only those.</remarks>
    public Job? Find(string uri, string? owner) =>
        Find(uri) is { } job && (job.Owner is null || string.Equals(job.Owner, owner, StringComparison.Ordinal)) ? job : null;

    /// <summary>Whether a URI names a job of this server, found or not.</summary>
    internal bool IsJobUri(string uri) => uri.StartsWith($"job://{_server}/", StringComparison.Ordinal);

    private static JobEnding Ending<T>(Answer<T> answer, JsonTypeInfo<T> resultType)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return answer.TryGet(out var value)
            ? new JobEnding(JobState.Succeeded, Result: JsonSerializer.SerializeToElement(value, resultType))
            : new JobEnding(JobState.Failed, answer.Error, DetailsOf(answer.Details), answer.Hint, answer.Type);
    }

    /// <summary>What a failed work learned before it stopped, as the result of its failed job; null when it learned nothing.</summary>
    internal static JsonElement? DetailsOf(JsonNode? details)
    {
        if (details is null)
            return null;

        return JsonSerializer.SerializeToElement(details, ToolAnswer.NodeInfo);
    }

    private Job Run(string kind, string? owner, Func<IProgress<JobProgress>, CancellationToken, Task<JobEnding>> work)
    {
        var uri = $"job://{_server}/{kind}/{Guid.NewGuid():N}";
        var job = new Job(uri, owner, _clock.GetUtcNow(), report => Changed?.Invoke(report));

        _jobs[uri] = job;
        _ = RunAsync(job, kind, work);

        return job;
    }

    private async Task RunAsync(Job job, string kind, Func<IProgress<JobProgress>, CancellationToken, Task<JobEnding>> work)
    {
        await Task.Yield();
        await job.RunAsync(work, failure => ToolFailures.FailureOf(kind, failure, Services, internalHint: null), _stopping, _clock, Keep).ConfigureAwait(false);
    }

    /// <summary>Keeps an ended job, forgetting its owner's oldest past <see cref="Kept"/> and the server's oldest past <see cref="KeptInAll"/>.</summary>
    private void Keep(Job job)
    {
        lock (_gate)
        {
            string owner = job.Owner ?? string.Empty;
            if (!_endedOf.TryGetValue(owner, out var theirs))
            {
                theirs = new Queue<LinkedListNode<Job>>();
                _endedOf[owner] = theirs;
            }

            theirs.Enqueue(_ended.AddLast(job));

            if (theirs.Count > Kept)
                Forget(theirs.Peek());

            if (_ended.Count > KeptInAll)
                Forget(_ended.First!);
        }
    }

    /// <summary>Forgets an ended job, which is always the oldest its owner has kept: both lists hold jobs in the order they ended.</summary>
    private void Forget(LinkedListNode<Job> oldest)
    {
        string owner = oldest.Value.Owner ?? string.Empty;
        var theirs = _endedOf[owner];

        theirs.Dequeue();
        if (theirs.Count == 0)
            _endedOf.Remove(owner);

        _ended.Remove(oldest);
        _jobs.TryRemove(oldest.Value.Uri, out _);
    }
}

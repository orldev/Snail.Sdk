using Microsoft.Extensions.Hosting;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>Raises the thread pool's minimum of worker threads as the host starts, never lowering it.</summary>
/// <param name="workers">The worker threads the pool makes without waiting.</param>
internal sealed class ReadyThreads(int workers) : IHostedLifecycleService
{
    /// <summary>The worker threads the pool makes without waiting.</summary>
    public int Workers { get; } = workers;

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        Raise(Workers);

        return Task.CompletedTask;
    }

    /// <summary>Raises the minimum of worker threads to <paramref name="workers"/>, keeping a larger one and the completion-port minimum.</summary>
    internal static void Raise(int workers)
    {
        ThreadPool.GetMinThreads(out var current, out var completions);

        if (current < workers)
            ThreadPool.SetMinThreads(workers, completions);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

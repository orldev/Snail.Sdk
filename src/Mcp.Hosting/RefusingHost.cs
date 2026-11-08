using Microsoft.Extensions.Hosting;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The host the builder made, whose start says options that failed their validation as a <see cref="StartRefusedException"/>.</summary>
/// <param name="host">The host the builder made.</param>
internal sealed class RefusingHost(IHost host) : IHost, IAsyncDisposable
{
    /// <inheritdoc/>
    public IServiceProvider Services => host.Services;

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (StartRefusal.Of(failure) is { } refused)
        {
            throw refused;
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default) => host.StopAsync(cancellationToken);

    /// <inheritdoc/>
    public void Dispose() => host.Dispose();

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (host is IAsyncDisposable disposable)
            return disposable.DisposeAsync();

        host.Dispose();

        return ValueTask.CompletedTask;
    }
}

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>The host the builder made, whose start says options that failed their validation, or a setting the binder could not convert, as a <see cref="StartRefusedException"/>.</summary>
/// <remarks>
/// A setting is named by the variable of the server the entry assembly carries, as <see cref="McpEndpoint"/> names its
/// own, and by its key alone in a program that names no server — a host built under the test runner.
/// </remarks>
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
        catch (Exception failure) when (StartRefusal.Of(failure, ServerSettings.NameOf(Assembly.GetEntryAssembly()), host.Services.GetService<IConfiguration>()) is { } refused)
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

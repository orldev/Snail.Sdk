using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Snail.Sdk.Mcp.Http;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>A stdio server whose client is gone stops by itself; a busy one, a shared one and one told not to stay up.</summary>
public sealed class IdleShutdownTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(60);

    private readonly ManualClock _clock = new();
    private readonly Lifetime _lifetime = new();
    private bool _isBusy;

    [Fact]
    public void TryStop_BeforeTheQuietPeriod_KeepsTheServer()
    {
        var shutdown = Watching(new McpActivity(_clock));

        _clock.Advance(Quiet - TimeSpan.FromSeconds(1));

        Assert.False(shutdown.TryStop());
        Assert.False(_lifetime.IsStopped);
    }

    [Fact]
    public void TryStop_AfterTheQuietPeriod_StopsTheServer()
    {
        var shutdown = Watching(new McpActivity(_clock));

        _clock.Advance(Quiet);

        Assert.True(shutdown.TryStop());
        Assert.True(_lifetime.IsStopped);
    }

    [Fact]
    public void TryStop_AMessageWithinThePeriod_KeepsTheServerUp()
    {
        var activity = new McpActivity(_clock);
        var shutdown = Watching(activity);

        _clock.Advance(Quiet - TimeSpan.FromSeconds(1));
        activity.Heard();
        _clock.Advance(Quiet - TimeSpan.FromSeconds(1));

        Assert.False(shutdown.TryStop());

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(shutdown.TryStop());
    }

    /// <summary>A plugin's bridge connected is work no MCP message shows; when it ends, the server waits a whole period again.</summary>
    [Fact]
    public void TryStop_WhileTheServerIsBusy_KeepsItUpAndStartsTheQuietAgainAfter()
    {
        var shutdown = Watching(new McpActivity(_clock));
        _isBusy = true;

        _clock.Advance(Quiet * 3);

        Assert.False(shutdown.TryStop());

        _isBusy = false;
        _clock.Advance(Quiet - TimeSpan.FromSeconds(1));

        Assert.False(shutdown.TryStop());

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(shutdown.TryStop());
    }

    [Fact]
    public async Task ExecuteAsync_TurnedOffByZero_EndsAtOnceAndKeepsTheServer()
    {
        using var shutdown = new IdleShutdown(new McpActivity(_clock), TimeSpan.Zero, () => false, isSingleSession: true, _clock, _lifetime, NullLogger<IdleShutdown>.Instance);

        await shutdown.StartAsync(CancellationToken.None);
        await shutdown.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(_lifetime.IsStopped);
    }

    /// <summary>The program is started by a client over a stream, as a stdio client starts it, and then hears nothing.</summary>
    [Fact]
    public async Task AddIdleShutdown_OverAStreamNobodySpeaksOn_StopsTheServer()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var builder = McpHost.CreateBuilder([]);

        builder.Services.AddMcpServer().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());
        builder.Services.AddIdleShutdown(TimeSpan.FromMilliseconds(200));

        using var host = builder.Build();
        var stopping = new TaskCompletionSource();
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        await host.StartAsync();
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(stopping.Task.IsCompletedSuccessfully);

        await host.StopAsync();
    }

    /// <summary>The one server every session shares is never stopped between sessions, so a program of both transports registers the shutdown alike.</summary>
    [Fact]
    public async Task AddIdleShutdown_OverHttp_LeavesTheServerRunning()
    {
        var builder = McpHttpHost.CreateBuilder([], new Uri($"http://127.0.0.1:{FreePort()}/"));

        builder.Services.AddMcpServer().WithLocalHttpTransport();
        builder.Services.AddIdleShutdown(TimeSpan.FromMilliseconds(100));

        await using var app = builder.Build();
        app.MapLocalMcp();
        await app.StartAsync();
        var shutdown = Assert.Single(app.Services.GetServices<IHostedService>().OfType<IdleShutdown>());

        await shutdown.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.False(app.Lifetime.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public void AddIdleShutdown_CalledAgain_TheLaterPeriodAndVetoStand()
    {
        var builder = McpHost.CreateBuilder([]);
        builder.Services.AddSingleton(new Plugin { IsConnected = true });

        builder.Services.AddIdleShutdown(TimeSpan.FromMinutes(5));
        builder.Services.AddIdleShutdown(TimeSpan.FromMinutes(60), services => services.GetRequiredService<Plugin>().IsConnected);

        using var host = builder.Build();
        var shutdown = Assert.Single(host.Services.GetServices<IHostedService>().OfType<IdleShutdown>());

        Assert.Equal(TimeSpan.FromMinutes(60), shutdown.After);
        Assert.True(shutdown.IsBusy);
    }

    [Fact]
    public void AddIdleShutdown_ANegativePeriod_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => McpHost.CreateBuilder([]).Services.AddIdleShutdown(TimeSpan.FromSeconds(-1)));

    /// <summary>The trim and the shutdown hear the same messages through one filter.</summary>
    [Fact]
    public void AddIdleShutdown_BesideTheTrim_CountsMessagesOnce()
    {
        var builder = McpHost.CreateBuilder([]);

        builder.Services.AddIdleShutdown(TimeSpan.FromMinutes(60));

        Assert.Single(builder.Services, service => service.ServiceType == typeof(McpActivity));
    }

    private IdleShutdown Watching(McpActivity activity) =>
        new(activity, Quiet, () => _isBusy, isSingleSession: true, _clock, _lifetime, NullLogger<IdleShutdown>.Instance);

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class Plugin
    {
        public bool IsConnected { get; init; }
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public bool IsStopped { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => IsStopped = true;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}

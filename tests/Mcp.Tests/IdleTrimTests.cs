using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Snail.Sdk.Mcp.Tests;

public sealed class IdleTrimTests
{
    private const long Megabyte = 1024 * 1024;

    private static readonly TimeSpan Quiet = TimeSpan.FromMinutes(5);

    private readonly ManualClock _clock = new();
    private long _allocated;
    private int _compactions;

    [Fact]
    public void TryTrim_BeforeTheQuietPeriod_LeavesTheHeapAlone()
    {
        var trim = Started(new McpActivity(_clock));
        _allocated = 100 * Megabyte;

        _clock.Advance(Quiet - TimeSpan.FromSeconds(1));

        Assert.False(trim.TryTrim());
        Assert.Equal(0, _compactions);
    }

    [Fact]
    public void TryTrim_AfterTheQuietPeriod_CompactsTheHeap()
    {
        var trim = Started(new McpActivity(_clock));
        _allocated = 100 * Megabyte;

        _clock.Advance(Quiet);

        Assert.True(trim.TryTrim());
        Assert.Equal(1, _compactions);
    }

    [Fact]
    public void TryTrim_AMessageWithinThePeriod_StartsTheQuietAgain()
    {
        var activity = new McpActivity(_clock);
        var trim = Started(activity);
        _allocated = 100 * Megabyte;

        _clock.Advance(Quiet - TimeSpan.FromSeconds(1));
        activity.Heard();
        _clock.Advance(TimeSpan.FromSeconds(2));

        Assert.False(trim.TryTrim());

        _clock.Advance(Quiet);

        Assert.True(trim.TryTrim());
    }

    [Fact]
    public void TryTrim_UnderThirtyTwoMegabytesAllocated_LeavesTheHeapAlone()
    {
        var trim = Started(new McpActivity(_clock));
        _allocated = IdleTrim.WorthTrimming - 1;

        _clock.Advance(Quiet);

        Assert.False(trim.TryTrim());

        _allocated = IdleTrim.WorthTrimming;

        Assert.True(trim.TryTrim());
    }

    /// <summary>A server that keeps allocating while nobody calls it is paused once per period, never on every tick.</summary>
    [Fact]
    public void TryTrim_AgainWithinThePeriod_LeavesTheHeapAlone()
    {
        var trim = Started(new McpActivity(_clock));
        _allocated = 100 * Megabyte;
        _clock.Advance(Quiet);

        trim.TryTrim();
        _allocated += 100 * Megabyte;
        _clock.Advance(Quiet - TimeSpan.FromSeconds(1));

        Assert.False(trim.TryTrim());

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(trim.TryTrim());
        Assert.Equal(2, _compactions);
    }

    [Fact]
    public void TryTrim_NothingAllocatedSinceTheLastTrim_LeavesTheHeapAlone()
    {
        var trim = Started(new McpActivity(_clock));
        _allocated = 100 * Megabyte;
        _clock.Advance(Quiet);

        trim.TryTrim();
        _clock.Advance(Quiet * 3);

        Assert.False(trim.TryTrim());
        Assert.Equal(1, _compactions);
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData(" 30 ", 30)]
    [InlineData("0", 0)]
    [InlineData("-5", 0)]
    [InlineData(null, 300)]
    [InlineData("", 300)]
    [InlineData("five minutes", 300)]
    public void PeriodOf_TheVariable_IsItsSecondsOrFiveMinutes(string? variable, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), IdleTrim.PeriodOf(variable));

    [Fact]
    public async Task ExecuteAsync_TurnedOffByZero_EndsAtOnce()
    {
        using var trim = new IdleTrim(new McpActivity(_clock), IdleTrim.PeriodOf("0"), _clock, () => _allocated, Compact, NullLogger<IdleTrim>.Instance);

        await trim.StartAsync(CancellationToken.None);
        await trim.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(trim.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void CreateBuilder_WithTheVariable_TakesItsPeriod()
    {
        Environment.SetEnvironmentVariable(IdleTrim.Variable, "90");
        try
        {
            using var host = McpHost.CreateBuilder([]).Build();

            var trim = Assert.Single(host.Services.GetServices<IHostedService>().OfType<IdleTrim>());

            Assert.Equal(TimeSpan.FromSeconds(90), trim.Period);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IdleTrim.Variable, null);
        }
    }

    [Fact]
    public void AddIdleTrim_CalledAgain_RegistersOnce()
    {
        var builder = McpHost.CreateBuilder([]);

        builder.Services.AddIdleTrim();

        Assert.Single(builder.Services, service => service.ServiceType == typeof(McpActivity));
        Assert.Single(builder.Services, service => service.ServiceType == typeof(IHostedService));
    }

    /// <summary>The stdio host counts a message as the HTTP host does, through the filter on the MCP server's options.</summary>
    [Fact]
    public async Task Message_OverAStream_StartsTheQuietAgain()
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var builder = McpHost.CreateBuilder([]);

        builder.Services.AddMcpServer().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream());

        using var host = builder.Build();
        await host.StartAsync();
        var activity = host.Services.GetRequiredService<McpActivity>();

        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var quietBefore = activity.Quiet;

        await using var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));

        Assert.True(activity.Quiet < quietBefore);

        await host.StopAsync();
    }

    private IdleTrim Started(McpActivity activity) =>
        new(activity, Quiet, _clock, () => _allocated, Compact, NullLogger<IdleTrim>.Instance);

    private IdleTrim.Compaction Compact()
    {
        _compactions++;

        return new IdleTrim.Compaction(_allocated, 0, TimeSpan.FromMilliseconds(50));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}

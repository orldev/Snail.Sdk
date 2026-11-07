using System.Diagnostics;
using System.Net.Sockets;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The probe beside the tests, Snail.MCP.Probe, started over HTTP as a server's own tests start the server.</summary>
public sealed class HttpServerProgramTests
{
    private const string Assembly = "Snail.MCP.Probe";

    private const string Variable = "SNAIL_PROBE_SERVER";

    private static readonly TimeSpan Short = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task StartAsync_OfAServerWithAReadinessPath_ConnectsASession()
    {
        await using var server = await HttpServerProgram.StartAsync(Assembly, Variable);
        await using var session = await server.SessionAsync();

        var tools = await session.ListToolsAsync();

        Assert.Equal("query", Assert.Single(tools).Name);
        Assert.True(server.Address.IsLoopback);
        Assert.Equal(new Uri(server.Address, "/mcp"), server.Endpoint);
        Assert.Contains($"listening on {server.Address}", server.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionAsync_WithAQuery_OpensTheSessionWithThatQuery()
    {
        await using var server = await HttpServerProgram.StartAsync(Assembly, Variable);
        await using var session = await server.SessionAsync("root=/workspaces/0c2b9e1f/step-2");

        var answer = await session.CallToolAsync("query");

        Assert.Equal("?root=/workspaces/0c2b9e1f/step-2", answer.Text());
    }

    /// <summary>A server that reads only its port, as Snail.MCP.E2E does, is told the port alone.</summary>
    [Fact]
    public async Task StartAsync_WithAPortVariable_GivesThePortAlone()
    {
        await using var server = await HttpServerProgram.StartAsync(Assembly, Variable, addressVariable: "SNAIL_MCP_PROBE_PORT");

        Assert.Contains($"listening on http://127.0.0.1:{server.Address.Port}/", server.Log, StringComparison.Ordinal);
    }

    /// <summary>A server that maps no readiness path is ready once its MCP endpoint answers, whatever the answer.</summary>
    [Fact]
    public async Task StartAsync_WithoutAReadinessPath_WaitsForTheMcpEndpoint()
    {
        await using var server = await HttpServerProgram.StartAsync(Assembly, Variable, readiness: null);
        await using var session = await server.SessionAsync();

        Assert.NotNull(session.ServerInfo);
    }

    [Fact]
    public async Task StartAsync_OfAServerThatExitsAtStart_FailsAtOnce_WithItsStderr()
    {
        var watch = Stopwatch.StartNew();
        var environment = new Dictionary<string, string?> { ["SNAIL_MCP_PROBE_FAIL"] = "the probe was told not to start" };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => HttpServerProgram.StartAsync(Assembly, Variable, environment));

        Assert.Contains("exited with 3", failure.Message, StringComparison.Ordinal);
        Assert.Contains("the probe was told not to start", failure.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < Short, $"failed after {watch.Elapsed}");
    }

    /// <summary>A readiness path the server does not map is a mistake of the test, said at once rather than after the patience.</summary>
    [Fact]
    public async Task StartAsync_WithAReadinessPathTheServerDoesNotMap_FailsAtOnce()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => HttpServerProgram.StartAsync(Assembly, Variable, readiness: "/ready"));

        Assert.Contains("GET /ready with 404", failure.Message, StringComparison.Ordinal);
        Assert.Contains("readiness: null", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_OfAServerNeverReady_FailsAfterThePatience_WithItsStderr()
    {
        var timeout = await Assert.ThrowsAsync<TimeoutException>(
            () => HttpServerProgram.StartAsync(Assembly, Variable, null, null, "/unready", TimeSpan.FromSeconds(2), CancellationToken.None));

        Assert.Contains("did not answer GET /unready within 2 s", timeout.Message, StringComparison.Ordinal);
        Assert.Contains("listening on", timeout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_KillsTheServer_AndFreesItsPort()
    {
        var server = await HttpServerProgram.StartAsync(Assembly, Variable);
        var id = server.ProcessId;

        await server.DisposeAsync();
        await server.DisposeAsync();

        Assert.Throws<ArgumentException>(() => Process.GetProcessById(id));
        using var client = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => client.ConnectAsync(server.Address.Host, server.Address.Port));
    }

    [Fact]
    public void AddressVariable_OfAServer_IsItsUrlVariable_OfAnyOtherProgramMustBeNamed()
    {
        Assert.Equal("SNAIL_MCP_SECURITY_URL", HttpServerProgram.AddressVariableOf("Snail.MCP.Security"));
        Assert.Equal("SNAIL_MCP_AFTEREFFECTS_URL", HttpServerProgram.AddressVariableOf("Snail.MCP.AfterEffects"));
        Assert.Throws<ArgumentException>(() => HttpServerProgram.AddressVariableOf("Other.Program"));
    }

    [Fact]
    public void Free_IsALoopbackPortNothingListensOn()
    {
        var port = Ports.Free();
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, port);

        listener.Start();

        Assert.InRange(port, 1, ushort.MaxValue);
    }
}

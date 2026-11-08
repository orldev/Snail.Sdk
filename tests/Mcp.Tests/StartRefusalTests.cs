using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Snail.Sdk.Mcp.Http;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>Options that fail their validation at the start stop the server with a refusal in one line, never with a crash and a trace.</summary>
public sealed class StartRefusalTests
{
    private const string TokenMissing = "SNAIL_MCP_DEMO_TOKEN (or Token in the settings file) is not set; it is the key the plugin presents.";

    /// <summary>Snail.MCP.BuildBridge validates its options with ValidateOnStart: an OptionsValidationException out of the start was a crash with exit code 134.</summary>
    [Fact]
    public async Task StartAsync_OptionsValidatedOnStartThatFail_IsRefusedInOneLine()
    {
        var builder = McpHost.CreateBuilder([], Settings());

        builder.Services.AddOptions<Demo>().Validate(demo => demo.Token is not null, TokenMissing).Validate(demo => demo.Port > 0, "SNAIL_MCP_DEMO_PORT is '0';\nit is a port.").ValidateOnStart();
        using var host = builder.Build();

        var refused = await Assert.ThrowsAsync<StartRefusedException>(() => host.StartAsync());

        Assert.Equal($"{TokenMissing}; SNAIL_MCP_DEMO_PORT is '0'; it is a port.", refused.Message);
        Assert.IsType<OptionsValidationException>(refused.InnerException);
    }

    /// <summary>Options read for the first time by a hosted service as it starts fail the same start, and are refused the same way.</summary>
    [Fact]
    public async Task StartAsync_OptionsReadAsAHostedServiceStarts_IsRefusedInOneLine()
    {
        var builder = McpHost.CreateBuilder([], Settings());

        builder.Services.AddOptions<Demo>().Validate(demo => demo.Token is not null, TokenMissing);
        builder.Services.AddHostedService<Reading>();
        using var host = builder.Build();

        var refused = await Assert.ThrowsAsync<StartRefusedException>(() => host.StartAsync());

        Assert.Equal(TokenMissing, refused.Message);
    }

    /// <summary>Two kinds of options failing at once are one refusal that says both.</summary>
    [Fact]
    public async Task StartAsync_TwoOptionsThatFail_SaysBothInOneLine()
    {
        var builder = McpHost.CreateBuilder([], Settings());

        builder.Services.AddOptions<Demo>().Validate(_ => false, "first").ValidateOnStart();
        builder.Services.AddOptions<Other>().Validate(_ => false, "second").ValidateOnStart();
        using var host = builder.Build();

        var refused = await Assert.ThrowsAsync<StartRefusedException>(() => host.StartAsync());

        Assert.Equal("first; second", refused.Message);
        Assert.DoesNotContain('\n', refused.Message);
    }

    /// <summary>The HTTP hosts refuse the same way: their own builder, and a server's own web builder once it takes the endpoint (Snail.MCP.Blender, AfterEffects).</summary>
    [Fact]
    public async Task StartAsync_OverHttp_OptionsThatFail_IsRefusedInOneLine()
    {
        var endpoint = new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:0/"), ListensOnEveryInterface: false);
        var empty = McpHttpHost.CreateBuilder([], endpoint, Settings());
        var whole = WebApplication.CreateBuilder().UseMcpEndpoint(endpoint);

        foreach (var builder in new[] { empty, whole })
            builder.Services.AddOptions<Demo>().Validate(demo => demo.Token is not null, TokenMissing).ValidateOnStart();

        await using var emptyApp = empty.Build();
        await using var wholeApp = whole.Build();

        Assert.Equal(TokenMissing, (await Assert.ThrowsAsync<StartRefusedException>(() => emptyApp.StartAsync())).Message);
        Assert.Equal(TokenMissing, (await Assert.ThrowsAsync<StartRefusedException>(() => wholeApp.StartAsync())).Message);
    }

    /// <summary>A failure of the start that is not the settings' is thrown as it was.</summary>
    [Fact]
    public async Task StartAsync_AnotherFailure_IsThrownAsItWas()
    {
        var builder = McpHost.CreateBuilder([], Settings());

        builder.Services.AddHostedService<Failing>();
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal("the disk is gone", failure.Message);
    }

    /// <summary>Options that pass start the host, which stops and disposes as it would.</summary>
    [Fact]
    public async Task StartAsync_OptionsThatPass_StartsTheHost()
    {
        var builder = McpHost.CreateBuilder([], Settings());

        builder.Services.AddStartRefusal();
        builder.Services.AddOptions<Demo>().Configure(demo => demo.Token = "key").ValidateOnStart();
        await using var host = (IAsyncDisposable)builder.Build();
        var started = (IHost)host;

        await started.StartAsync();
        await started.StopAsync();

        Assert.Equal("key", started.Services.GetRequiredService<IOptions<Demo>>().Value.Token);
        Assert.Single(builder.Services, service => service.ServiceType == typeof(IHost));
    }

    /// <summary>A server's own refusal names what the operator set as McpEndpoint names its keys: the variable, and the key of the settings file.</summary>
    [Theory]
    [InlineData("git", "TimeoutSeconds", "SNAIL_MCP_GIT_TIMEOUT_SECONDS (or TimeoutSeconds in the settings file)")]
    [InlineData("blender", "Http:Url", "SNAIL_MCP_BLENDER_HTTP__URL (or Http:Url in the settings file)")]
    [InlineData("filesystem", "Snail:MaxFileBytes", "SNAIL_MCP_FILESYSTEM_SNAIL__MAX_FILE_BYTES (or Snail:MaxFileBytes in the settings file)")]
    public void NameOfSetting_IsTheVariableAndTheKeyOfTheSettingsFile(string server, string key, string expected) =>
        Assert.Equal(expected, ServerSettings.NameOfSetting(server, key));

    /// <summary>The variable a refusal names is the one the settings are read from.</summary>
    [Fact]
    public void NameOfSetting_NamesTheVariableTheSettingsAreReadFrom()
    {
        var variables = new System.Collections.Hashtable { ["SNAIL_MCP_GIT_TIMEOUT_SECONDS"] = "30" };

        var settings = new ConfigurationBuilder().AddServerSettings("git", ServerProgram.NoSettingsFile, variables).Build();

        Assert.StartsWith("SNAIL_MCP_GIT_TIMEOUT_SECONDS ", ServerSettings.NameOfSetting("git", "TimeoutSeconds"), StringComparison.Ordinal);
        Assert.Equal("30", settings["TimeoutSeconds"]);
    }

    /// <summary>The probe, a program that says a refusal in one line and exits with 1, writes nothing beyond its own line before the start and the refusal: the host's error line with the trace is left out.</summary>
    [Fact]
    public async Task Program_OptionsThatFailAtTheStart_WritesOneLineAndExitsWithOne()
    {
        var environment = new Dictionary<string, string?> { ["SNAIL_MCP_PROBE_TOKEN_REQUIRED"] = "true" };

        var exited = await Assert.ThrowsAsync<ServerExitedException>(() => HttpServerProgram.StartAsync("Snail.MCP.Probe", "SNAIL_PROBE_SERVER", environment));

        var lines = exited.Stderr.Split('\n');

        Assert.Equal(1, exited.ExitCode);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("listening on ", lines[0], StringComparison.Ordinal);
        Assert.Equal("SNAIL_MCP_PROBE_TOKEN (or Token in the settings file) is not set; the probe asks for one.", lines[1]);
    }

    private static IConfiguration Settings() => new ConfigurationBuilder().Build();

    private sealed class Demo
    {
        public string? Token { get; set; }

        public int Port { get; set; }
    }

    private sealed class Other;

    private sealed class Reading(IOptions<Demo> options) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.FromResult(options.Value);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Failing : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("the disk is gone");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

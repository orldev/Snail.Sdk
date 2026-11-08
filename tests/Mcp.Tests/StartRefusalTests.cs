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

        Assert.Equal($"{TokenMissing} SNAIL_MCP_DEMO_PORT is '0'; it is a port.", refused.Message);
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

    /// <summary>Failures written as sentences follow one another after a space, and bare fragments after a semicolon (Snail.MCP.BuildBridge read "… above zero.; SNAIL_MCP_…").</summary>
    [Fact]
    public async Task StartAsync_SentencesAndFragmentsThatFail_AreJoinedAsEachIsWritten()
    {
        var builder = McpHost.CreateBuilder([], Settings());

        builder.Services.AddOptions<Demo>()
            .Validate(_ => false, "SNAIL_MCP_DEMO_TIMEOUT is '0'; it is a number of seconds above zero.")
            .Validate(_ => false, "Is the port free?")
            .Validate(_ => false, "a fragment")
            .Validate(_ => false, "Done!")
            .ValidateOnStart();
        using var host = builder.Build();

        var refused = await Assert.ThrowsAsync<StartRefusedException>(() => host.StartAsync());

        Assert.Equal("SNAIL_MCP_DEMO_TIMEOUT is '0'; it is a number of seconds above zero. Is the port free? a fragment; Done!", refused.Message);
    }

    /// <summary>Options bound from a value their property's type cannot hold failed the start with the binder's InvalidOperationException, a crash with exit code 134 (Snail.MCP.Blender's Bridge:Port); under the test runner no server is named, so the key alone is.</summary>
    [Fact]
    public async Task StartAsync_OptionsBoundFromAValueOfTheWrongType_IsRefusedInOneLine()
    {
        var settings = Settings(("Bridge:Port", "abc"));
        var builder = McpHost.CreateBuilder([], settings);

        builder.Services.AddOptions<Demo>().Bind(settings).ValidateOnStart();
        using var host = builder.Build();

        var refused = await Assert.ThrowsAsync<StartRefusedException>(() => host.StartAsync());

        Assert.Equal("Bridge:Port is 'abc'; it is not a number (Int32).", refused.Message);
        Assert.IsType<InvalidOperationException>(refused.InnerException);
    }

    /// <summary>Options bound from a bad value and read by a hosted service as it starts are refused the same way, over HTTP too, and with a validation failing beside them both are said.</summary>
    [Fact]
    public async Task StartAsync_OverHttp_OptionsReadAsAHostedServiceStartsFromAValueOfTheWrongType_IsRefusedInOneLine()
    {
        var endpoint = new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:0/"), ListensOnEveryInterface: false);
        var settings = Settings(("Bridge:Port", "99999999999"));
        var builder = McpHttpHost.CreateBuilder([], endpoint, settings);

        builder.Services.AddOptions<Demo>().Bind(settings);
        builder.Services.AddHostedService<Reading>();
        await using var app = builder.Build();

        var refused = await Assert.ThrowsAsync<StartRefusedException>(() => app.StartAsync());

        Assert.Equal("Bridge:Port is '99999999999'; it is out of the range of Int32.", refused.Message);
    }

    /// <summary>An InvalidOperationException out of the start that the binder did not write stays the failure it was, even caused by a parse.</summary>
    [Fact]
    public async Task StartAsync_AnInvalidOperationOfAnotherShape_IsThrownAsItWas()
    {
        var builder = McpHost.CreateBuilder([], Settings());

        builder.Services.AddHostedService<Parsing>();
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.IsNotType<StartRefusedException>(failure);
        Assert.Equal("the cache is corrupt", failure.Message);
    }

    /// <summary>A server that binds its settings before any host exists (Snail.MCP.Blender's Peek, Git's and Documents' Read) gets the refusal naming the variable, as a program says it.</summary>
    [Theory]
    [InlineData("Bridge:Port", "abc", "SNAIL_MCP_BLENDER_BRIDGE__PORT (or Bridge:Port in the settings file) is 'abc'; it is not a number (Int32).")]
    [InlineData("Bridge:Port", "99999999999", "SNAIL_MCP_BLENDER_BRIDGE__PORT (or Bridge:Port in the settings file) is '99999999999'; it is out of the range of Int32.")]
    [InlineData("Snail:Bridge:Port", "x", "SNAIL_MCP_BLENDER_SNAIL__BRIDGE__PORT (or Snail:Bridge:Port in the settings file) is 'x'; it is not a number (Int32).")]
    [InlineData("IsVerbose", "maybe", "SNAIL_MCP_BLENDER_IS_VERBOSE (or IsVerbose in the settings file) is 'maybe'; it is not true or false (Boolean).")]
    [InlineData("Wait", "soon", "SNAIL_MCP_BLENDER_WAIT (or Wait in the settings file) is 'soon'; it is not a time span such as 00:05:00 (TimeSpan).")]
    [InlineData("Transport", "Carrier", "SNAIL_MCP_BLENDER_TRANSPORT (or Transport in the settings file) is 'Carrier'; it is not a valid McpTransport.")]
    [InlineData("Limit", "lots", "SNAIL_MCP_BLENDER_LIMIT (or Limit in the settings file) is 'lots'; it is not a number (Int64).")]
    public void Bind_AValueOfTheWrongType_IsRefusedNamingTheVariable(string key, string value, string expected)
    {
        var settings = Settings((key, value));
        var config = new Bound();

        var refused = Assert.Throws<StartRefusedException>(() => ServerSettings.Bind("blender", settings, read =>
        {
            read.GetSection("Snail").Bind(config);
            read.Bind(config);
        }));

        Assert.Equal(expected, refused.Message);
        Assert.IsType<InvalidOperationException>(refused.InnerException);
    }

    /// <summary>Settings that bind are bound as the server's own binding says.</summary>
    [Fact]
    public void Bind_SettingsThatBind_AreBound()
    {
        var settings = Settings(("Bridge:Port", "8400"), ("Snail:Wait", "00:00:05"));
        var config = new Bound();

        ServerSettings.Bind("blender", settings, read =>
        {
            read.GetSection("Snail").Bind(config);
            read.Bind(config);
        });

        Assert.Equal(8400, config.Bridge.Port);
        Assert.Equal(TimeSpan.FromSeconds(5), config.Wait);
    }

    /// <summary>The binding source generator, on for every native server, writes its own messages: .NET 10's carries the value, .NET 8's leaves it out, and the value is then read from the settings at the path.</summary>
    [Theory]
    [InlineData("Failed to convert configuration value 'abc' at 'Bridge:Port' to type 'System.Int32'.", "abc", "SNAIL_MCP_BLENDER_BRIDGE__PORT (or Bridge:Port in the settings file) is 'abc'; it is not a number (Int32).")]
    [InlineData("Failed to convert configuration value at 'Bridge:Port' to type 'System.Int32'.", "abc", "SNAIL_MCP_BLENDER_BRIDGE__PORT (or Bridge:Port in the settings file) is 'abc'; it is not a number (Int32).")]
    [InlineData("Failed to convert configuration value at 'Bridge:Port' to type 'System.Nullable`1[System.Int32]'.", null, "SNAIL_MCP_BLENDER_BRIDGE__PORT (or Bridge:Port in the settings file) is not a number (Int32).")]
    [InlineData("Error_FailedBinding, a, b, Bridge:Port, System.Int32", "a, b", "SNAIL_MCP_BLENDER_BRIDGE__PORT (or Bridge:Port in the settings file) is 'a, b'; it is not a number (Int32).")]
    public void Bind_TheGeneratorsAndTheResourceKeysMessages_AreRefusedNamingTheVariable(string message, string? value, string expected)
    {
        var settings = value is null ? Settings() : Settings(("Bridge:Port", value));

        var refused = Assert.Throws<StartRefusedException>(() => ServerSettings.Bind("blender", settings, _ => throw new InvalidOperationException(message, new FormatException("The input string was not in a correct format."))));

        Assert.Equal(expected, refused.Message);
    }

    /// <summary>A failure of the binding of another shape is thrown as it was: a binder's message without a parse behind it, or a parse under a message the binder does not write.</summary>
    [Theory]
    [InlineData("Failed to convert configuration value 'abc' at 'Bridge:Port' to type 'System.Int32'.", false)]
    [InlineData("Cannot create instance of type 'Bound' because it is missing a public parameterless constructor.", true)]
    [InlineData("Failed to convert configuration value 'abc' at 'Bridge:Port' to type 'System.Int32'. And more.", true)]
    public void Bind_AnInvalidOperationOfAnotherShape_IsThrownAsItWas(string message, bool isCausedByAParse)
    {
        var failure = isCausedByAParse ? new InvalidOperationException(message, new FormatException("bad")) : new InvalidOperationException(message);

        var thrown = Assert.Throws<InvalidOperationException>(() => ServerSettings.Bind("blender", Settings(), _ => throw failure));

        Assert.Same(failure, thrown);
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

    /// <summary>The probe binds its settings before it builds its host, as Snail.MCP.Blender does: a value that is no number is said in one line naming the variable, before the host says anything.</summary>
    [Fact]
    public async Task Program_ASettingOfTheWrongTypeBoundBeforeTheHost_WritesOneLineAndExitsWithOne()
    {
        var environment = new Dictionary<string, string?> { ["SNAIL_MCP_PROBE_RETRIES"] = "many" };

        var exited = await Assert.ThrowsAsync<ServerExitedException>(() => HttpServerProgram.StartAsync("Snail.MCP.Probe", "SNAIL_PROBE_SERVER", environment));

        Assert.Equal(1, exited.ExitCode);
        Assert.Equal("SNAIL_MCP_PROBE_RETRIES (or Retries in the settings file) is 'many'; it is not a number (Int32).", exited.Stderr);
    }

    /// <summary>Options the probe's host binds as it starts, from a value that is no time span, are said in one line with no trace: the probe names no server in its assembly, so the key alone is named.</summary>
    [Fact]
    public async Task Program_ASettingOfTheWrongTypeBoundAsTheHostStarts_WritesOneLineAndExitsWithOne()
    {
        var environment = new Dictionary<string, string?> { ["SNAIL_MCP_PROBE_DELAY"] = "soon" };

        var exited = await Assert.ThrowsAsync<ServerExitedException>(() => HttpServerProgram.StartAsync("Snail.MCP.Probe", "SNAIL_PROBE_SERVER", environment));

        var lines = exited.Stderr.Split('\n');

        Assert.Equal(1, exited.ExitCode);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("listening on ", lines[0], StringComparison.Ordinal);
        Assert.Equal("Delay is 'soon'; it is not a time span such as 00:05:00 (TimeSpan).", lines[1]);
        Assert.DoesNotContain("   at ", exited.Stderr, StringComparison.Ordinal);
    }

    private static IConfiguration Settings() => new ConfigurationBuilder().Build();

    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();

    private sealed class Demo
    {
        public string? Token { get; set; }

        public int Port { get; set; }

        public Bridge Bridge { get; set; } = new();
    }

    private sealed class Bridge
    {
        public int Port { get; set; }
    }

    private sealed class Bound
    {
        public Bridge Bridge { get; set; } = new();

        public bool IsVerbose { get; set; }

        public TimeSpan Wait { get; set; }

        public McpTransport Transport { get; set; }

        public long Limit { get; set; }
    }

    private sealed class Other;

    private sealed class Reading(IOptions<Demo> options) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.FromResult(options.Value);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Parsing : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("the cache is corrupt", new FormatException("bad"));

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Failing : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("the disk is gone");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

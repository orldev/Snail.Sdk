using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The stdio host of a program built on Snail.Sdk.Mcp reads its server's settings by itself.</summary>
public sealed class McpHostTests
{
    /// <summary>
    /// The settings are read through <c>AddServerSettings(name)</c>, which moves the server's folders from the snail names
    /// first; a server built on Snail.Sdk.Mcp no longer calls the move itself.
    /// </summary>
    [Fact]
    public void CreateBuilder_ForAProgramThatNamesItsServer_ReadsItsSettings()
    {
        Environment.SetEnvironmentVariable("SNAIL_MCP_HOSTDEMO_GREETING", "hello");
        try
        {
            var builder = McpHost.CreateBuilder([], Server("s3l-mcp-hostdemo"));

            Assert.Equal("hello", builder.Configuration["Greeting"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SNAIL_MCP_HOSTDEMO_GREETING", null);
        }
    }

    /// <summary>
    /// The settings the program read — Snail.MCP.Playwright's with its browser defaults added — are the host's, read once,
    /// and a test under the runner, whose entry assembly names no server, gives them the same way.
    /// </summary>
    [Fact]
    public void CreateBuilder_WithTheSettingsTheProgramRead_IsTheirHost()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Greeting"] = "hello", ["BrowserDefaults:Headless"] = "true" })
            .Build();

        var builder = McpHost.CreateBuilder([], settings);

        Assert.Equal("hello", builder.Configuration["Greeting"]);
        Assert.Equal("true", builder.Configuration["BrowserDefaults:Headless"]);
    }

    [Fact]
    public void CreateBuilder_WithTheSettingsTheProgramRead_LogsAtTheirLevels()
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Demo"] = "Error" })
            .Build();

        using var host = McpHost.CreateBuilder([], settings).Build();
        var logger = host.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Demo");

        Assert.False(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning));
        Assert.True(logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error));
    }

    /// <summary>Snail.MCP.CleanUp: a call during a census waited 7 to 8 s for a worker until the minimum was raised to 64.</summary>
    [Fact]
    public async Task AddReadyThreads_RaisesTheMinimumOfWorkers_AsTheHostStarts()
    {
        ThreadPool.GetMinThreads(out var workers, out var completions);
        var builder = McpHost.CreateBuilder([], new ConfigurationBuilder().Build());

        builder.Services.AddReadyThreads(workers + 8);

        try
        {
            using var host = builder.Build();
            await host.StartAsync();
            ThreadPool.GetMinThreads(out var raised, out var raisedCompletions);
            await host.StopAsync();

            Assert.Equal(workers + 8, raised);
            Assert.Equal(completions, raisedCompletions);
        }
        finally
        {
            ThreadPool.SetMinThreads(workers, completions);
        }
    }

    [Fact]
    public void AddReadyThreads_NeverLowersTheMinimum()
    {
        ThreadPool.GetMinThreads(out var workers, out _);

        ReadyThreads.Raise(1);
        ThreadPool.GetMinThreads(out var after, out _);

        Assert.Equal(workers, after);
    }

    [Fact]
    public void AddReadyThreads_CalledAgain_KeepsTheLargestNumberAndOneService()
    {
        var services = new ServiceCollection();

        services.AddReadyThreads(64).AddReadyThreads(16).AddReadyThreads(96);

        var ready = Assert.Single(services, service => service.ServiceType == typeof(ReadyThreads));

        Assert.Equal(96, Assert.IsType<ReadyThreads>(ready.ImplementationInstance).Workers);
        Assert.Single(services, service => service.ServiceType == typeof(IHostedService));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddReadyThreads_NoWorkers_IsRefused(int workers) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceCollection().AddReadyThreads(workers));

    private static Assembly Server(string command) =>
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("HostDemo"),
            AssemblyBuilderAccess.Run,
            [new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, ["Snail.Sdk.Mcp.ServerName", command])]);
}

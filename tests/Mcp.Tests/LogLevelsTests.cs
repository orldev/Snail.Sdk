using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Snail.Sdk.Mcp.Http;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>The log levels a server's settings name, honoured by both hosts, over defaults that silence the MCP SDK's information.</summary>
public sealed class LogLevelsTests
{
    /// <summary>The MCP SDK writes three lines at information for every call (Snail.MCP.Documents, 2026-11-07).</summary>
    [Fact]
    public void Levels_NamedNowhere_KeepTheMcpSdkToWarnings_AndEverythingElseToInformation()
    {
        using var services = Services(new());
        var loggers = services.GetRequiredService<ILoggerFactory>();

        Assert.False(loggers.CreateLogger("ModelContextProtocol.Server.McpServer").IsEnabled(LogLevel.Information));
        Assert.True(loggers.CreateLogger("ModelContextProtocol.Server.McpServer").IsEnabled(LogLevel.Warning));
        Assert.False(loggers.CreateLogger("Microsoft.AspNetCore.Routing").IsEnabled(LogLevel.Information));
        Assert.True(loggers.CreateLogger("Snail.MCP.Demo").IsEnabled(LogLevel.Information));
        Assert.False(loggers.CreateLogger("Snail.MCP.Demo").IsEnabled(LogLevel.Debug));
    }

    [Fact]
    public void Levels_NamedInTheSettings_WinOverTheDefaults()
    {
        using var services = Services(new() { ["Logging:LogLevel:ModelContextProtocol"] = "Information", ["Logging:LogLevel:Default"] = "Debug" });
        var loggers = services.GetRequiredService<ILoggerFactory>();

        Assert.True(loggers.CreateLogger("ModelContextProtocol.Server.McpServer").IsEnabled(LogLevel.Information));
        Assert.True(loggers.CreateLogger("Snail.MCP.Demo").IsEnabled(LogLevel.Debug));
    }

    /// <summary>An image's Dockerfile sets the plain .NET names as its defaults; the server's settings file and its variables win over them.</summary>
    [Fact]
    public void Levels_OfThePlainVariables_AreReadUnderTheServersSettings()
    {
        var settings = Settings(new() { ["Logging:LogLevel:Mixed"] = "Warning" });
        var variables = new Dictionary<string, string>
        {
            ["Logging__LogLevel__Mixed"] = "Debug",
            ["Logging__LogLevel__Microsoft.AspNetCore"] = "Error",
            ["SNAIL_MCP_DEMO_LOGGING__LOGLEVEL__OTHER"] = "Trace",
        };

        var levels = McpHost.LevelsOf(settings, variables);

        Assert.Equal("Warning", levels["LogLevel:Mixed"]);
        Assert.Equal("Error", levels["LogLevel:Microsoft.AspNetCore"]);
        Assert.Null(levels["LogLevel:Other"]);
    }

    /// <summary>The stdio host reads the levels from the server's own variables, SNAIL_MCP_&lt;NAME&gt;_LOGGING__LOGLEVEL__….</summary>
    [Fact]
    public void CreateBuilder_ForAProgramThatNamesItsServer_HonoursTheLevelsOfItsVariables()
    {
        Environment.SetEnvironmentVariable("SNAIL_MCP_LOGDEMO_LOGGING__LOG_LEVEL__CHATTY", "Debug");
        try
        {
            using var host = McpHost.CreateBuilder([], Server("s3l-mcp-logdemo")).Build();
            var loggers = host.Services.GetRequiredService<ILoggerFactory>();

            Assert.True(loggers.CreateLogger("Chatty.Part").IsEnabled(LogLevel.Debug));
            Assert.False(loggers.CreateLogger("ModelContextProtocol.Server.McpServer").IsEnabled(LogLevel.Information));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SNAIL_MCP_LOGDEMO_LOGGING__LOG_LEVEL__CHATTY", null);
        }
    }

    /// <summary>The HTTP host honours the plain variables an image sets, Logging__LogLevel__…, which went unread before.</summary>
    [Fact]
    public async Task CreateBuilder_OverHttp_HonoursThePlainVariablesOfAnImage()
    {
        Environment.SetEnvironmentVariable("Logging__LogLevel__QuietDemo", "Error");
        try
        {
            await using var app = McpHttpHost.CreateBuilder([], new Uri("http://127.0.0.1:9/")).Build();
            var loggers = app.Services.GetRequiredService<ILoggerFactory>();

            Assert.False(loggers.CreateLogger("QuietDemo").IsEnabled(LogLevel.Warning));
            Assert.True(loggers.CreateLogger("QuietDemo").IsEnabled(LogLevel.Error));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Logging__LogLevel__QuietDemo", null);
        }
    }

    private static ServiceProvider Services(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.UseStderr().UseLevelsFrom(Settings(values)));

        return services.BuildServiceProvider();
    }

    private static IConfiguration Settings(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Assembly Server(string command) =>
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("LogDemo"),
            AssemblyBuilderAccess.Run,
            [new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, ["Snail.Sdk.Mcp.ServerName", command])]);
}

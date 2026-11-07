using System.Reflection;
using System.Reflection.Emit;

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

    private static Assembly Server(string command) =>
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("HostDemo"),
            AssemblyBuilderAccess.Run,
            [new CustomAttributeBuilder(typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!, ["Snail.Sdk.Mcp.ServerName", command])]);
}

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Http;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>Where a server answers, read from either of the fleet's two ways of saying it, and the host built for it.</summary>
public sealed class McpEndpointTests
{
    private static readonly Uri Default = new("http://127.0.0.1:9300/");

    [Fact]
    public void Read_NothingSet_IsStdio()
    {
        var endpoint = Read([]);

        Assert.Equal(new McpEndpoint(McpTransport.Stdio, Default, ListensOnEveryInterface: false), endpoint);
    }

    [Theory]
    [InlineData("Http")]
    [InlineData("http")]
    [InlineData(" HTTP ")]
    public void Read_TransportHttp_IsHttpAtTheDefaultUrl(string transport)
    {
        var endpoint = Read(new() { ["Transport"] = transport });

        Assert.Equal(new McpEndpoint(McpTransport.Http, Default, ListensOnEveryInterface: false), endpoint);
    }

    [Fact]
    public void Read_TransportAndUrl_IsHttpAtTheUrl()
    {
        var endpoint = Read(new() { ["Transport"] = "Http", ["Url"] = "http://127.0.0.1:9400/" });

        Assert.Equal(new Uri("http://127.0.0.1:9400/"), endpoint.Url);
        Assert.Equal(McpTransport.Http, endpoint.Transport);
    }

    /// <summary>Snail.MCP.Blender and Snail.MCP.AfterEffects keep the address in their Http section.</summary>
    [Fact]
    public void Read_UrlInTheHttpSection_IsTheUrl_AndTheRootOneWins()
    {
        var sectioned = Read(new() { ["Transport"] = "Http", ["Http:Url"] = "http://127.0.0.1:8080" });
        var both = Read(new() { ["Transport"] = "Http", ["Http:Url"] = "http://127.0.0.1:8080", ["Url"] = "http://127.0.0.1:9400/" });

        Assert.Equal(8080, sectioned.Url.Port);
        Assert.Equal(9400, both.Url.Port);
    }

    /// <summary>Snail.MCP.Git, FileSystem, Documents, Playwright and Roslyn: a port alone means HTTP.</summary>
    [Fact]
    public void Read_APortAlone_IsHttpOnThatPortOfLoopback()
    {
        var endpoint = Read(new() { ["Port"] = "8818" });

        Assert.Equal(new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:8818/"), ListensOnEveryInterface: false), endpoint);
    }

    [Fact]
    public void Read_APortOfZero_IsStdio() =>
        Assert.Equal(McpTransport.Stdio, Read(new() { ["Port"] = "0" }).Transport);

    [Fact]
    public void Read_APortAndTheFlag_ListensOnEveryInterface()
    {
        var endpoint = Read(new() { ["Port"] = "8818", ["ListensOnEveryInterface"] = "true" });

        Assert.True(endpoint.ListensOnEveryInterface);
        Assert.Equal(8818, endpoint.Url.Port);
    }

    /// <summary>A transport that is set decides; a port then only replaces the port of the URL.</summary>
    [Fact]
    public void Read_TransportStdioBesideAPort_IsStdio() =>
        Assert.Equal(McpTransport.Stdio, Read(new() { ["Transport"] = "Stdio", ["Port"] = "8818" }).Transport);

    [Fact]
    public void Read_APortBesideAUrl_ReplacesItsPort() =>
        Assert.Equal(new Uri("http://127.0.0.1:8818/"), Read(new() { ["Url"] = "http://127.0.0.1:9400/", ["Port"] = "8818" }).Url);

    /// <summary>Snail.MCP.Sandbox, Blender and AfterEffects images name 0.0.0.0, which means every interface.</summary>
    [Theory]
    [InlineData("http://0.0.0.0:8826/")]
    [InlineData("http://[::]:8826/")]
    public void Read_TheUnspecifiedAddress_ListensOnEveryInterface(string url) =>
        Assert.True(Read(new() { ["Transport"] = "Http", ["Url"] = url }).ListensOnEveryInterface);

    /// <summary>Snail.MCP.Security: inside a container the published port does not reach loopback.</summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    public void Read_InsideAContainer_ListensOnEveryInterface(string? container, bool listensOnEveryInterface)
    {
        var endpoint = McpEndpoint.Read(Settings(new() { ["Transport"] = "Http" }), Default, container);

        Assert.Equal(listensOnEveryInterface, endpoint.ListensOnEveryInterface);
    }

    [Theory]
    [InlineData("Transport", "carrier pigeon")]
    [InlineData("Transport", "2")]
    [InlineData("Port", "eighty")]
    [InlineData("Port", "70000")]
    [InlineData("Url", "127.0.0.1:9400")]
    [InlineData("Url", "ftp://127.0.0.1/")]
    [InlineData("ListensOnEveryInterface", "yes")]
    public void Read_AValueTheKeyCannotMean_IsRefusedNamingIt(string key, string value)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => Read(new() { [key] = value }));

        Assert.Contains(key, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(value, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateBuilder_ALoopbackEndpoint_ListensOnItsUrl()
    {
        var builder = McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:9400/"), ListensOnEveryInterface: false));

        Assert.Equal("http://127.0.0.1:9400/", builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey));
    }

    [Fact]
    public void CreateBuilder_AnEndpointOnEveryInterface_ListensOnItsPortEverywhere()
    {
        var builder = McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Http, new Uri("http://0.0.0.0:8826/"), ListensOnEveryInterface: true));

        Assert.Equal("http://*:8826", builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey));
    }

    [Fact]
    public void CreateBuilder_AStdioEndpoint_IsRefused() =>
        Assert.Throws<ArgumentException>(() => McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Stdio, Default, ListensOnEveryInterface: false)));

    /// <summary>An address beyond loopback is never served without the flag or a container that asks for it.</summary>
    [Fact]
    public void CreateBuilder_AnAddressBeyondLoopbackWithoutTheFlag_IsRefused() =>
        Assert.Throws<ArgumentException>(() => McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Http, new Uri("http://192.168.1.5:9400/"), ListensOnEveryInterface: false)));

    private static McpEndpoint Read(Dictionary<string, string?> values) => McpEndpoint.Read(Settings(values), Default, containerVariable: null);

    private static IConfiguration Settings(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

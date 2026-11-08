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

    /// <summary>Over stdio no port is opened, so the flag means nothing: an operator who set it meant HTTP, and is told so naming the variable (Snail.MCP.FileSystem, Git refused it themselves).</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("Stdio", null)]
    [InlineData(null, "0")]
    [InlineData("Stdio", "8818")]
    public void Read_TheFlagOverStdio_IsRefusedNamingIt(string? transport, string? port)
    {
        var settings = Settings(new() { ["Transport"] = transport, ["Port"] = port, ["ListensOnEveryInterface"] = "true" });

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(settings, Default, isHttpOnly: false, containerVariable: null, server: "demo"));

        Assert.StartsWith("SNAIL_MCP_DEMO_LISTENS_ON_EVERY_INTERFACE (or ListensOnEveryInterface in the settings file) is 'true'; the server answers over stdio", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("SNAIL_MCP_DEMO_TRANSPORT (or Transport in the settings file) is Http", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Only the operator's setting is refused over stdio: a container, a flag set to false and a URL that says where HTTP would listen all start a stdio server.</summary>
    [Theory]
    [InlineData("ListensOnEveryInterface", "false", null)]
    [InlineData("Url", "http://0.0.0.0:8818/", null)]
    [InlineData("Url", "http://192.168.1.5:8818/", null)]
    [InlineData("Transport", "Stdio", "true")]
    public void Read_OverStdio_WhatOpensNoPort_IsNotRefused(string key, string value, string? container)
    {
        var settings = Settings(new() { [key] = value });

        var endpoint = McpEndpoint.Read(settings, Default, isHttpOnly: false, container, server: "demo", new McpEndpointOptions { Reach = McpReach.LoopbackOnly });

        Assert.Equal(McpTransport.Stdio, endpoint.Transport);
    }

    /// <summary>An image that sets the flag beside its port or transport serves HTTP as before, and so does a flag kept under the fallback section.</summary>
    [Theory]
    [InlineData("Port", "8818")]
    [InlineData("Transport", "Http")]
    public void Read_TheFlagOverHttp_ListensOnEveryInterface(string key, string value)
    {
        var atRoot = Read(new() { [key] = value, ["ListensOnEveryInterface"] = "true" });
        var inSection = Read(new() { [$"Snail:{key}"] = value, ["Snail:ListensOnEveryInterface"] = "true" }, "Snail");

        Assert.True(atRoot.ListensOnEveryInterface);
        Assert.True(inSection.ListensOnEveryInterface);
    }

    /// <summary>The flag under the fallback section is refused over stdio too, named where it was found.</summary>
    [Fact]
    public void Read_TheFlagInTheSectionOverStdio_IsRefusedNamingIt()
    {
        var settings = Settings(new() { ["Snail:ListensOnEveryInterface"] = "true" });

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(settings, Default, isHttpOnly: false, containerVariable: null, server: "demo", "Snail"));

        Assert.StartsWith("SNAIL_MCP_DEMO_SNAIL__LISTENS_ON_EVERY_INTERFACE (or Snail:ListensOnEveryInterface in the settings file) is 'true'", refusal.Message, StringComparison.Ordinal);
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
        var endpoint = McpEndpoint.Read(Settings(new() { ["Transport"] = "Http" }), Default, isHttpOnly: false, container, server: null);

        Assert.Equal(listensOnEveryInterface, endpoint.ListensOnEveryInterface);
    }

    /// <summary>A server that answers only over HTTP says so once, and a port in its settings still moves it.</summary>
    [Theory]
    [InlineData(null, null, 9300)]
    [InlineData("Http", null, 9300)]
    [InlineData(null, "9400", 9400)]
    public void ReadHttp_SettingsThatMeanHttp_AreHttp(string? transport, string? port, int expected)
    {
        var endpoint = ReadHttp(new() { ["Transport"] = transport, ["Port"] = port });

        Assert.Equal(McpTransport.Http, endpoint.Transport);
        Assert.Equal(expected, endpoint.Url.Port);
    }

    /// <summary>Read as stdio, these came out an endpoint no HTTP builder takes, refused later without naming the variable (Snail.MCP.Sandbox).</summary>
    [Theory]
    [InlineData("Transport", "Stdio")]
    [InlineData("Port", "0")]
    public void ReadHttp_SettingsThatMeanStdio_AreRefusedAtTheRead(string key, string value)
    {
        var refusal = Assert.Throws<StartRefusedException>(() => ReadHttp(new() { [key] = value }));

        Assert.Contains($"{key} is '{value}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("only over HTTP", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The operator set a variable, so a refusal names it, and the key of the settings file beside it.</summary>
    [Theory]
    [InlineData("Url", "127.0.0.1:9400", "SNAIL_MCP_DEMO_URL (or Url in the settings file) is '127.0.0.1:9400'")]
    [InlineData("Http:Url", "ftp://x/", "SNAIL_MCP_DEMO_HTTP__URL (or Http:Url in the settings file) is 'ftp://x/'")]
    [InlineData("ListensOnEveryInterface", "yes", "SNAIL_MCP_DEMO_LISTENS_ON_EVERY_INTERFACE (or ListensOnEveryInterface in the settings file) is 'yes'")]
    [InlineData("Transport", "Stdio", "SNAIL_MCP_DEMO_TRANSPORT (or Transport in the settings file) is 'Stdio'")]
    public void ReadHttp_AValueOfANamedServer_IsRefusedNamingItsVariable(string key, string value, string expected)
    {
        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { [key] = value }), Default, isHttpOnly: true, containerVariable: null, server: "demo"));

        Assert.StartsWith(expected, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A server that keeps the keys in a section passes the section, and the refusal names the key under it.</summary>
    [Fact]
    public void Read_ASection_IsRefusedNamingTheKeyUnderIt()
    {
        var section = Settings(new() { ["Server:Port"] = "eighty" }).GetSection("Server");

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(section, Default, isHttpOnly: false, containerVariable: null, server: "demo"));

        Assert.StartsWith("SNAIL_MCP_DEMO_SERVER__PORT (or Server:Port in the settings file) is 'eighty'", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Snail.MCP.Playwright, FileSystem, Documents and Git keep their settings in a Snail section, and their operators wrote the endpoint there too.</summary>
    [Fact]
    public void Read_AKeyOnlyInTheFallbackSection_IsRead()
    {
        var endpoint = Read(new() { ["Snail:Port"] = "8822", ["Snail:ListensOnEveryInterface"] = "true" }, "Snail");

        Assert.Equal(new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:8822/"), ListensOnEveryInterface: true), endpoint);
    }

    [Fact]
    public void Read_AKeyAtTheRootAndInTheFallbackSection_IsTheRootOne() =>
        Assert.Equal(8818, Read(new() { ["Port"] = "8818", ["Snail:Port"] = "8822" }, "Snail").Url.Port);

    /// <summary>Each key is looked for on its own: a transport at the root and an address in the section are both read.</summary>
    [Fact]
    public void Read_KeysSplitBetweenTheRootAndTheFallbackSection_AreEachRead()
    {
        var endpoint = Read(new() { ["Transport"] = "Http", ["Snail:Http:Url"] = "http://127.0.0.1:9400/" }, "Snail");

        Assert.Equal(new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:9400/"), ListensOnEveryInterface: false) { UrlSetting = "Snail:Http:Url" }, endpoint);
    }

    /// <summary>An address at the root, in either spelling, wins over any address in the section.</summary>
    [Fact]
    public void Read_AnAddressAtTheRoot_WinsOverTheSectionsUrl() =>
        Assert.Equal(8080, Read(new() { ["Transport"] = "Http", ["Http:Url"] = "http://127.0.0.1:8080/", ["Snail:Url"] = "http://127.0.0.1:9400/" }, "Snail").Url.Port);

    [Fact]
    public void Read_WithoutAFallbackSection_LooksAtTheRootAlone() =>
        Assert.Equal(McpTransport.Stdio, Read(new() { ["Snail:Port"] = "8822" }).Transport);

    /// <summary>#20 with a section: the operator wrote the key in the section, so the refusal names the section's variable and key.</summary>
    [Theory]
    [InlineData("Snail:Port", "eighty", "SNAIL_MCP_DEMO_SNAIL__PORT (or Snail:Port in the settings file) is 'eighty'")]
    [InlineData("Snail:Http:Url", "ftp://x/", "SNAIL_MCP_DEMO_SNAIL__HTTP__URL (or Snail:Http:Url in the settings file) is 'ftp://x/'")]
    [InlineData("Snail:Transport", "Stdio", "SNAIL_MCP_DEMO_SNAIL__TRANSPORT (or Snail:Transport in the settings file) is 'Stdio'")]
    [InlineData("Snail:ListensOnEveryInterface", "yes", "SNAIL_MCP_DEMO_SNAIL__LISTENS_ON_EVERY_INTERFACE (or Snail:ListensOnEveryInterface in the settings file) is 'yes'")]
    public void ReadHttp_AValueInTheFallbackSection_IsRefusedNamingItsVariable(string key, string value, string expected)
    {
        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { [key] = value }), Default, isHttpOnly: true, containerVariable: null, server: "demo", fallbackSection: "Snail"));

        Assert.StartsWith(expected, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A value at the root is the one read, so it is the one a refusal names, whatever the section holds.</summary>
    [Fact]
    public void Read_ARootValueBesideTheSection_IsRefusedNamingTheRootVariable()
    {
        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { ["Port"] = "eighty", ["Snail:Port"] = "8822" }), Default, isHttpOnly: false, containerVariable: null, server: "demo", fallbackSection: "Snail"));

        Assert.StartsWith("SNAIL_MCP_DEMO_PORT (or Port in the settings file) is 'eighty'", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Read_ABlankFallbackSection_IsRefused(string section)
    {
        Assert.Throws<ArgumentException>(() => McpEndpoint.Read(Settings([]), Default, section));
        Assert.Throws<ArgumentException>(() => McpEndpoint.ReadHttp(Settings([]), Default, section));
    }

    [Fact]
    public void ReadHttp_TheFallbackSection_IsReadByThePublicCall() =>
        Assert.Equal(8822, McpEndpoint.ReadHttp(Settings(new() { ["Snail:Port"] = "8822" }), Default, "Snail").Url.Port);

    /// <summary>Snail.MCP.E2E under launchd listens on every interface outside a container: its default URL says 0.0.0.0, and a loopback URL narrows it.</summary>
    [Fact]
    public void ReadHttp_ADefaultUrlOnEveryInterface_ListensOnEveryInterface_UntilAUrlNarrowsIt()
    {
        var everywhere = new Uri("http://0.0.0.0:8830/");

        var byDefault = McpEndpoint.Read(Settings(new() { ["Port"] = "8840" }), everywhere, isHttpOnly: true, containerVariable: null, server: null);
        var narrowed = McpEndpoint.Read(Settings(new() { ["Url"] = "http://127.0.0.1:8840/" }), everywhere, isHttpOnly: true, containerVariable: null, server: null);

        Assert.Equal(new McpEndpoint(McpTransport.Http, new Uri("http://0.0.0.0:8840/"), ListensOnEveryInterface: true), byDefault);
        Assert.Equal(new McpEndpoint(McpTransport.Http, new Uri("http://127.0.0.1:8840/"), ListensOnEveryInterface: false) { UrlSetting = "Url" }, narrowed);
    }

    [Theory]
    [InlineData("Transport", "carrier pigeon")]
    [InlineData("Transport", "2")]
    [InlineData("Port", "eighty")]
    [InlineData("Port", "70000")]
    [InlineData("Url", "127.0.0.1:9400")]
    [InlineData("Url", "ftp://127.0.0.1/")]
    [InlineData("Url", "https://127.0.0.1/")]
    [InlineData("Port", "-1")]
    [InlineData("ListensOnEveryInterface", "yes")]
    public void Read_AValueTheKeyCannotMean_IsRefusedNamingIt(string key, string value)
    {
        var refusal = Assert.Throws<StartRefusedException>(() => Read(new() { [key] = value }));

        Assert.Contains(key, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(value, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Snail.MCP.Blender signed links with Http:Url while SNAIL_MCP_BLENDER_URL moved the listener; the endpoint names the key it read.</summary>
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("http://127.0.0.1:9400/", null, "Url")]
    [InlineData(null, "http://127.0.0.1:9400/", "Http:Url")]
    [InlineData("http://127.0.0.1:9400/", "http://127.0.0.1:9500/", "Url")]
    public void Read_TheKeyOfTheAddress_IsNamed(string? url, string? httpUrl, string? expected) =>
        Assert.Equal(expected, Read(new() { ["Url"] = url, ["Http:Url"] = httpUrl }).UrlSetting);

    [Fact]
    public void Read_TheKeyOfTheAddressInTheFallbackSection_IsNamedWithIt() =>
        Assert.Equal("Snail:Url", Read(new() { ["Snail:Url"] = "http://127.0.0.1:9400/" }, "Snail").UrlSetting);

    /// <summary>A host name was served on loopback without a word; an address is an IP address or localhost.</summary>
    [Fact]
    public void Read_AnHttpUrlAtAHostName_IsRefusedNamingIt()
    {
        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { ["Transport"] = "Http", ["Url"] = "http://studio.lan:9400/" }), Default, isHttpOnly: false, containerVariable: null, server: "demo"));

        Assert.StartsWith("SNAIL_MCP_DEMO_URL (or Url in the settings file) is 'http://studio.lan:9400/'; ", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Over stdio nothing listens, so an address it would listen on is not held to anything.</summary>
    [Fact]
    public void Read_AStdioServerWithAUrlBeyondItsReach_IsStdio() =>
        Assert.Equal(McpTransport.Stdio, Read(new() { ["Url"] = "http://0.0.0.0:9400/" }, reach: McpReach.LoopbackOnly).Transport);

    /// <summary>Snail.MCP.Blender and AfterEffects listen on a LAN address behind their tokens; a server that guards itself keeps that.</summary>
    [Fact]
    public void Read_ALanAddressOfAnUnrestrictedServer_IsListenedOnAsNamed()
    {
        var endpoint = Read(new() { ["Transport"] = "Http", ["Url"] = "http://192.168.1.5:9400/" });

        Assert.Equal(new McpEndpoint(McpTransport.Http, new Uri("http://192.168.1.5:9400/"), ListensOnEveryInterface: false) { UrlSetting = "Url" }, endpoint);
    }

    /// <summary>#86: a keyless server held to loopback refuses each way of opening it, naming what the operator set.</summary>
    [Theory]
    [InlineData(McpReach.LoopbackOnly, "ListensOnEveryInterface", "true", "SNAIL_MCP_DEMO_LISTENS_ON_EVERY_INTERFACE (or ListensOnEveryInterface in the settings file) is 'true'; ")]
    [InlineData(McpReach.LoopbackOnly, "Url", "http://0.0.0.0:9400/", "SNAIL_MCP_DEMO_URL (or Url in the settings file) is 'http://0.0.0.0:9400/'; ")]
    [InlineData(McpReach.LoopbackOnly, "Url", "http://[::]:9400/", "SNAIL_MCP_DEMO_URL (or Url in the settings file) is 'http://[::]:9400/'; ")]
    [InlineData(McpReach.LoopbackOnly, "Http:Url", "http://192.168.1.5:9400/", "SNAIL_MCP_DEMO_HTTP__URL (or Http:Url in the settings file) is 'http://192.168.1.5:9400/'; ")]
    [InlineData(McpReach.LoopbackOrContainer, "ListensOnEveryInterface", "true", "SNAIL_MCP_DEMO_LISTENS_ON_EVERY_INTERFACE (or ListensOnEveryInterface in the settings file) is 'true'; ")]
    [InlineData(McpReach.LoopbackOrContainer, "Url", "http://0.0.0.0:9400/", "SNAIL_MCP_DEMO_URL (or Url in the settings file) is 'http://0.0.0.0:9400/'; ")]
    [InlineData(McpReach.LoopbackOrContainer, "Url", "http://10.0.0.7:9400/", "SNAIL_MCP_DEMO_URL (or Url in the settings file) is 'http://10.0.0.7:9400/'; ")]
    public void Read_BeyondTheReach_IsRefusedNamingTheVariable(McpReach reach, string key, string value, string expected)
    {
        var options = new McpEndpointOptions { Reach = reach };

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { ["Transport"] = "Http", [key] = value }), Default, isHttpOnly: false, containerVariable: null, server: "demo", options));

        Assert.StartsWith(expected, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("loopback alone", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>#123: a server that ships an image may open every interface inside a container, and a refusal outside one says so, where it said "loopback alone" as to a server that may not.</summary>
    [Theory]
    [InlineData(McpReach.LoopbackOrContainer, true)]
    [InlineData(McpReach.LoopbackOnly, false)]
    public void Read_BeyondTheReachOutsideAContainer_SaysWhetherAContainerOpensIt(McpReach reach, bool saysContainer)
    {
        var options = new McpEndpointOptions { Reach = reach };

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { ["Port"] = "9400", ["ListensOnEveryInterface"] = "true" }), Default, isHttpOnly: false, containerVariable: null, server: "demo", options));

        Assert.Equal(saysContainer, refusal.Message.Contains("on every interface only inside one", StringComparison.Ordinal));
    }

    /// <summary>#134: a test reads the endpoint under the test runner, whose assembly names no server; the name in the options spells the variables all the same.</summary>
    [Fact]
    public void Read_TheServerNamedInTheOptions_SpellsTheVariablesOfTheRefusal()
    {
        var options = new McpEndpointOptions { Reach = McpReach.LoopbackOnly, Server = "demo" };

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.ReadHttp(Settings(new() { ["Url"] = "http://0.0.0.0:9400/" }), Default, options));

        Assert.StartsWith("SNAIL_MCP_DEMO_URL (or Url in the settings file) is 'http://0.0.0.0:9400/'; ", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Under the test runner and without a name in the options, a refusal names the key alone, as for any program not built on Snail.Sdk.Mcp.</summary>
    [Fact]
    public void Read_NoServerNamedUnderTheTestRunner_NamesTheKeyAlone()
    {
        var options = new McpEndpointOptions { Reach = McpReach.LoopbackOnly };

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { ["Port"] = "9400", ["Url"] = "http://0.0.0.0:9400/" }), Default, options));

        Assert.StartsWith("Url is 'http://0.0.0.0:9400/'; ", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ABlankServerInTheOptions_IsRefused() =>
        Assert.Throws<ArgumentException>(() => McpEndpoint.Read(Settings([]), Default, new McpEndpointOptions { Server = " " }));

    /// <summary>#130: a refusal at the start is the one type a program catches to say it in one line, and still an InvalidOperationException for a server that catches that.</summary>
    [Fact]
    public void Read_AValueTheKeyCannotMean_IsAStartRefusal_ThatStaysAnInvalidOperation()
    {
        var refusal = Assert.ThrowsAny<InvalidOperationException>(() => Read(new() { ["Port"] = "x" }));

        Assert.IsType<StartRefusedException>(refusal);
    }

    /// <summary>Snail.MCP.Memory ships no image: a start inside a container would open every interface, so it is refused naming the variable.</summary>
    [Fact]
    public void Read_InsideAContainer_LoopbackOnly_IsRefusedNamingTheContainerVariable()
    {
        var options = new McpEndpointOptions { Reach = McpReach.LoopbackOnly };

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings(new() { ["Port"] = "9400" }), Default, isHttpOnly: false, containerVariable: "true", server: "demo", options));

        Assert.StartsWith("DOTNET_RUNNING_IN_CONTAINER is 'true'; ", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Snail.MCP.Git and FileSystem ship an image: inside it the published port is the boundary, and an image that names 0.0.0.0 keeps working.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("http://0.0.0.0:9400/")]
    public void Read_InsideAContainer_LoopbackOrContainer_ListensOnEveryInterface(string? url)
    {
        var options = new McpEndpointOptions { Reach = McpReach.LoopbackOrContainer };

        var endpoint = McpEndpoint.Read(Settings(new() { ["Port"] = "9400", ["Url"] = url }), Default, isHttpOnly: false, containerVariable: "true", server: "demo", options);

        Assert.True(endpoint.ListensOnEveryInterface);
        Assert.Equal(9400, endpoint.Url.Port);
    }

    [Theory]
    [InlineData(McpReach.LoopbackOnly, "http://127.0.0.1:9400/")]
    [InlineData(McpReach.LoopbackOnly, "http://[::1]:9400/")]
    [InlineData(McpReach.LoopbackOnly, "http://localhost:9400/")]
    [InlineData(McpReach.LoopbackOrContainer, "http://127.0.0.1:9400/")]
    public void Read_OnLoopback_IsWithinEveryReach(McpReach reach, string url)
    {
        var endpoint = Read(new() { ["Transport"] = "Http", ["Url"] = url }, reach: reach);

        Assert.False(endpoint.ListensOnEveryInterface);
        Assert.Equal(new Uri(url), endpoint.Url);
    }

    /// <summary>A server that listens on every interface by default cannot also hold itself to loopback; the refusal says the default is the server's.</summary>
    [Fact]
    public void ReadHttp_ADefaultBeyondTheReach_IsRefusedNamingTheDefault()
    {
        var options = new McpEndpointOptions { Reach = McpReach.LoopbackOnly };

        var refusal = Assert.Throws<StartRefusedException>(() => McpEndpoint.Read(Settings([]), new Uri("http://0.0.0.0:8830/"), isHttpOnly: true, containerVariable: null, server: "demo", options));

        Assert.StartsWith("The server's default address is 'http://0.0.0.0:8830/'; ", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ThePublicCallsWithOptions_ReadTheFallbackSectionAndTheReach()
    {
        var options = new McpEndpointOptions { FallbackSection = "Snail", Reach = McpReach.LoopbackOnly };

        Assert.Equal(8822, McpEndpoint.Read(Settings(new() { ["Snail:Port"] = "8822" }), Default, options).Url.Port);
        Assert.Throws<StartRefusedException>(() => McpEndpoint.ReadHttp(Settings(new() { ["Snail:ListensOnEveryInterface"] = "true" }), Default, options));
    }

    [Fact]
    public void Read_ABlankFallbackSectionInTheOptions_IsRefused() =>
        Assert.Throws<ArgumentException>(() => McpEndpoint.Read(Settings([]), Default, new McpEndpointOptions { FallbackSection = " " }));

    [Fact]
    public void CreateBuilder_AStdioEndpoint_IsRefused() =>
        Assert.Throws<ArgumentException>(() => McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Stdio, Default, ListensOnEveryInterface: false)));

    /// <summary>The host cannot listen on a name; how far an address may reach is the read's to decide.</summary>
    [Fact]
    public void CreateBuilder_AnEndpointAtAHostName_IsRefused() =>
        Assert.Throws<ArgumentException>(() => McpHttpHost.CreateBuilder([], new McpEndpoint(McpTransport.Http, new Uri("http://studio.lan:9400/"), ListensOnEveryInterface: false)));

    private static McpEndpoint Read(Dictionary<string, string?> values, string? fallbackSection = null, McpReach reach = McpReach.Unrestricted) =>
        McpEndpoint.Read(Settings(values), Default, isHttpOnly: false, containerVariable: null, server: null, new McpEndpointOptions { FallbackSection = fallbackSection, Reach = reach });

    private static McpEndpoint ReadHttp(Dictionary<string, string?> values) => McpEndpoint.Read(Settings(values), Default, isHttpOnly: true, containerVariable: null, server: null);

    private static IConfiguration Settings(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

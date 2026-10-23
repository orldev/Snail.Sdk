using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Snail.Web.Template.Tests;

/// <summary>The application believes the X-Forwarded headers of the configured proxy, and of nobody else.</summary>
public sealed class ProxyTests
{
    [Fact]
    public void ForwardedHeaders_OfTheConfiguredNetwork_AreBelieved()
    {
        using var application = new ApplicationFactory(("Proxy:Networks:0", "172.18.0.0/16"));

        var forwarded = application.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal("172.18.0.0/16", Assert.Single(forwarded.KnownIPNetworks).ToString());
        Assert.Empty(forwarded.KnownProxies);
    }

    /// <summary>A typo in the proxy's network would quietly trust nobody, so the application refuses to start.</summary>
    [Fact]
    public void Start_WithAMalformedNetwork_IsRefused()
    {
        using var application = new ApplicationFactory(("Proxy:Networks:0", "172.18.0.0/99"));

        var refused = Assert.Throws<OptionsValidationException>(() => application.CreateClient());

        Assert.Contains("172.18.0.0/99", refused.Message, StringComparison.Ordinal);
    }
}

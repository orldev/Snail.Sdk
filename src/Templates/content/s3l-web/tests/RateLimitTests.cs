using System.Net;

namespace Snail.Web.Template.Tests;

/// <summary>A client that asks more often than the window allows is told to wait.</summary>
public sealed class RateLimitTests
{
    [Fact]
    public async Task Requests_PastTheLimit_AreRefusedWith429()
    {
        using var application = new ApplicationFactory(("RateLimit:PermitLimit", "2"), ("RateLimit:WindowSeconds", "60"));
        using var client = application.CreateClient();
#if (IsApi)
        var limited = new Uri("/api/greetings/Ada", UriKind.Relative);
#else
        var limited = new Uri("/", UriKind.Relative);
#endif

        await client.GetAsync(limited);
        await client.GetAsync(limited);
        var third = await client.GetAsync(limited);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }
}

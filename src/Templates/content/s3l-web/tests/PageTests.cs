using System.Net;

namespace Snail.Web.Template.Tests;

/// <summary>The pages render as a browser first receives them.</summary>
public sealed class PageTests : IDisposable
{
    private readonly ApplicationFactory _application = new();

    [Fact]
    public async Task Home_Requested_RendersTheApplication()
    {
        using var client = _application.CreateClient();

        var page = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("<h1 class=\"text-3xl font-semibold\">Snail.Web.Template</h1>", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownAddress_Requested_ShowsTheNotFoundPage()
    {
        using var client = _application.CreateClient();

        var answer = await client.GetAsync(new Uri("/nowhere", UriKind.Relative));
        var page = await answer.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Contains("There is nothing at this address.", page, StringComparison.Ordinal);
    }

    public void Dispose() => _application.Dispose();
}

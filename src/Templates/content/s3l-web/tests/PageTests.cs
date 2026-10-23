using System.Net;
#if (localization)
using Microsoft.AspNetCore.Mvc.Testing;
#endif

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
#if (localization)

    [Fact]
    public async Task UnknownAddress_InTheBrowsersLanguage_IsExplainedInIt()
    {
        using var client = _application.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9");

        var answer = await client.GetAsync(new Uri("/nowhere", UriKind.Relative));
        var page = await answer.Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"ru\">", page, StringComparison.Ordinal);
        Assert.Contains("По этому адресу ничего нет.", page, StringComparison.Ordinal);
    }

    /// <summary>The choice outweighs the browser's language, on every page after it.</summary>
    [Fact]
    public async Task Language_Chosen_IsKeptForTheNextPages()
    {
        using var client = _application.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU");

        await client.GetAsync(new Uri("/culture/es?returnUrl=%2F", UriKind.Relative));
        var answer = await client.GetAsync(new Uri("/nowhere", UriKind.Relative));
        var page = await answer.Content.ReadAsStringAsync();

        Assert.Contains("<html lang=\"es\">", page, StringComparison.Ordinal);
        Assert.Contains("No hay nada en esta dirección.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Language_Chosen_LeadsBackToThePageItWasChosenOn()
    {
        using var client = _application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var answer = await client.GetAsync(new Uri("/culture/es?returnUrl=%2Fnowhere%3Fpage%3D2", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, answer.StatusCode);
        Assert.Equal("/nowhere?page=2", answer.Headers.Location?.OriginalString);
    }

    /// <summary>An address off the site would make the application a redirector for any link: the choice leads home instead.</summary>
    [Fact]
    public async Task Language_ChosenWithAnAddressOffTheSite_LeadsHome()
    {
        using var client = _application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var answer = await client.GetAsync(new Uri("/culture/es?returnUrl=https%3A%2F%2Fexample.com%2F", UriKind.Relative));

        Assert.Equal("/", answer.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Language_TheApplicationDoesNotSpeak_IsNotKept()
    {
        using var client = _application.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var answer = await client.GetAsync(new Uri("/culture/de?returnUrl=%2F", UriKind.Relative));

        Assert.False(answer.Headers.Contains("Set-Cookie"));
    }
#endif

    public void Dispose() => _application.Dispose();
}

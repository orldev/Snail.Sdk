using System.Net.Http.Json;
#if (localization)
using Microsoft.AspNetCore.Localization;
#endif
using Snail.Web.Template.Greetings;

namespace Snail.Web.Template.Tests;

/// <summary>The greetings API answers as a client reads it.</summary>
public sealed class GreetingTests : IDisposable
{
    private readonly ApplicationFactory _application = new();

    [Fact]
    public async Task Greeting_ForAName_GreetsIt()
    {
        using var client = _application.CreateClient();

        var greeting = await client.GetFromJsonAsync<Greeting>(new Uri("/api/greetings/Ada", UriKind.Relative));

        Assert.Equal(new Greeting("Hello, Ada!"), greeting);
    }
#if (localization)

    [Theory]
    [InlineData("ru-RU,ru;q=0.9,en;q=0.8", "Привет, Ada!")]
    [InlineData("es", "¡Hola, Ada!")]
    [InlineData("de-DE", "Hello, Ada!")]
    public async Task Greeting_InTheClientsLanguage_IsWrittenInItOrInEnglish(string acceptLanguage, string expected)
    {
        using var client = _application.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(acceptLanguage);

        var greeting = await client.GetFromJsonAsync<Greeting>(new Uri("/api/greetings/Ada", UriKind.Relative));

        Assert.Equal(new Greeting(expected), greeting);
    }

    [Fact]
    public async Task Greeting_WithALanguageChosen_PrefersItToTheClientsHeader()
    {
        using var client = _application.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU");
        client.DefaultRequestHeaders.Add("Cookie", $"{CookieRequestCultureProvider.DefaultCookieName}={CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("es"))}");

        var greeting = await client.GetFromJsonAsync<Greeting>(new Uri("/api/greetings/Ada", UriKind.Relative));

        Assert.Equal(new Greeting("¡Hola, Ada!"), greeting);
    }
#endif

    public void Dispose() => _application.Dispose();
}

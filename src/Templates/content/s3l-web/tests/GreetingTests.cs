using System.Net.Http.Json;
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

    public void Dispose() => _application.Dispose();
}

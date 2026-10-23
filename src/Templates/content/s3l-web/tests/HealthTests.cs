using System.Net;

namespace Snail.Web.Template.Tests;

/// <summary>The application says whether it runs.</summary>
public sealed class HealthTests : IDisposable
{
    private readonly ApplicationFactory _application = new();

    [Fact]
    public async Task Health_WhileRunning_AnswersOk()
    {
        using var client = _application.CreateClient();

        var answer = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
    }

    public void Dispose() => _application.Dispose();
}

using System.Net;

namespace Snail.Web.Template.Tests;

/// <summary>The user's own account is for the signed-in user alone.</summary>
public sealed class AccountTests : IDisposable
{
    private readonly ApplicationFactory _application = new();

    [Fact]
    public async Task Me_WithoutSigningIn_IsRefusedWith401()
    {
        using var client = _application.CreateClient();

        var answer = await client.GetAsync(new Uri("/account/me", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }

    public void Dispose() => _application.Dispose();
}

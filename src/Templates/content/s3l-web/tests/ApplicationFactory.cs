using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Snail.Web.Template.Tests;

/// <summary>The application hosted in memory, with settings of a test's own over its appsettings.</summary>
public sealed class ApplicationFactory(params (string Key, string Value)[] settings) : WebApplicationFactory<Program>
{
#if (UsesJwt)
    /// <summary>A signing key of the 64 characters HMAC-SHA512 asks for, for tests only.</summary>
    public const string SigningKey = "tests-only-signing-key-of-sixty-four-characters-for-hmac-sha-512";

#endif
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
#if (UsesJwt)
        builder.UseSetting("Jwt:SecretKey", SigningKey);

#endif
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
    }
}

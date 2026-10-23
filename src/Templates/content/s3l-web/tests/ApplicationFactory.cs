using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
#if (rabbit)
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
#endif

namespace Snail.Web.Template.Tests;

/// <summary>The application hosted in memory, with settings of a test's own over its appsettings.</summary>
#if (rabbit)
/// <remarks>
/// Without the services that open the RabbitMQ connection and declare its exchange when the host starts: a test has no
/// broker, and the host would refuse to start without one. The topology is declared against a real broker, in compose.
/// </remarks>
#endif
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
#if (rabbit)

        builder.ConfigureTestServices(services =>
        {
            var broker = services.Where(service => service.ServiceType == typeof(IHostedService) && IsRabbit(service.ImplementationType)).ToList();

            foreach (var service in broker)
                services.Remove(service);
        });
#endif
    }
#if (rabbit)

    private static bool IsRabbit(Type? implementation) =>
        implementation?.Namespace?.StartsWith("Snail.Toolkit.RabbitMQ", StringComparison.Ordinal) is true;
#endif
}

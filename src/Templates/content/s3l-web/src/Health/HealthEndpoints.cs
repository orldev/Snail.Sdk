using Microsoft.AspNetCore.Diagnostics.HealthChecks;
#if (mongo)
using Snail.Toolkit.Mongodb.DependencyInjection;
#endif

namespace Snail.Web.Template.Health;

/// <summary>Whether the application runs, and whether what it depends on answers.</summary>
internal static class HealthEndpoints
{
    /// <summary>The tag of the checks the readiness endpoint runs: a dependency's.</summary>
    public const string Ready = "ready";

    extension(IServiceCollection services)
    {
        /// <summary>The health checks; each dependency adds its own, tagged ready.</summary>
        public IServiceCollection AddHealth()
        {
#if (mongo)
            services.AddHealthChecks().AddMongodb(tags: [Ready]);
#else
            services.AddHealthChecks();
#endif

            return services;
        }
    }

    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>/health answers while the process runs; /health/ready also asks what the application depends on.</summary>
        public IEndpointRouteBuilder MapHealth()
        {
            endpoints.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(Ready) });

            return endpoints;
        }
    }
}

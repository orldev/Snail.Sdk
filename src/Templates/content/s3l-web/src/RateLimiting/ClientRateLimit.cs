using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Snail.Web.Template.RateLimiting;

/// <summary>A fixed window of requests per client address, for the endpoints that name the policy.</summary>
internal static class ClientRateLimit
{
    /// <summary>The policy an endpoint names to be limited per client address.</summary>
    public const string PolicyName = "per-client";

    extension(IServiceCollection services)
    {
        /// <summary>The policy, its limits read from configuration and checked at start; a refused request gets 429.</summary>
        public IServiceCollection AddClientRateLimit(IConfiguration configuration)
        {
            services.AddOptions<ClientRateLimitOptions>()
                .Bind(configuration.GetSection(ClientRateLimitOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.AddPolicy(PolicyName, PartitionOf);
            });

            return services;
        }
    }

    private static RateLimitPartition<string> PartitionOf(HttpContext context)
    {
        var limit = context.RequestServices.GetRequiredService<IOptions<ClientRateLimitOptions>>().Value;
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var window = new FixedWindowRateLimiterOptions { PermitLimit = limit.PermitLimit, Window = TimeSpan.FromSeconds(limit.WindowSeconds), QueueLimit = 0 };

        return RateLimitPartition.GetFixedWindowLimiter(client, _ => window);
    }
}

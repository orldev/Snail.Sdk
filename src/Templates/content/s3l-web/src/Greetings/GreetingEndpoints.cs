using Snail.Web.Template.RateLimiting;

namespace Snail.Web.Template.Greetings;

/// <summary>The greetings API.</summary>
internal static class GreetingEndpoints
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>GET /api/greetings/{name}: the greeting for a name, limited per client.</summary>
        public IEndpointRouteBuilder MapGreetings()
        {
            var greetings = endpoints.MapGroup("/api/greetings").RequireRateLimiting(ClientRateLimit.PolicyName);

            greetings.MapGet("/{name}", (string name) => TypedResults.Ok(Greeting.For(name)));

            return endpoints;
        }
    }
}

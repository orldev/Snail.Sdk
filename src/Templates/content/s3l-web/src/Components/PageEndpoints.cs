using Snail.Web.Template.RateLimiting;

namespace Snail.Web.Template.Components;

/// <summary>The application's pages: Blazor Server components, rendered interactively over a circuit.</summary>
internal static class PageEndpoints
{
    extension(IServiceCollection services)
    {
        /// <summary>The Razor components and their interactive server rendering.</summary>
        public IServiceCollection AddPages()
        {
            services.AddRazorComponents().AddInteractiveServerComponents();

            return services;
        }
    }

    extension(WebApplication app)
    {
        /// <summary>
        /// An error page outside development, with HSTS, and the not-found page for every status without a body: the
        /// pages show what went wrong, the log keeps why.
        /// </summary>
        public WebApplication UsePageErrors()
        {
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/error", createScopeForErrors: true);
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found");

            return app;
        }
    }

    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>The static assets and the pages, limited per client.</summary>
        public IEndpointRouteBuilder MapPages()
        {
            endpoints.MapStaticAssets();
            endpoints.MapRazorComponents<App>().AddInteractiveServerRenderMode().RequireRateLimiting(ClientRateLimit.PolicyName);

            return endpoints;
        }
    }
}

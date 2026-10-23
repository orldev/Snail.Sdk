#if (UsesAuth)
using Snail.Web.Template.Account;
#endif
#if (IsBlazor)
using Snail.Web.Template.Components;
#endif
#if (IsApi)
using Snail.Web.Template.Greetings;
#endif
using Snail.Web.Template.Health;
#if (localization)
using Snail.Web.Template.Localization;
#endif
#if (rabbit)
using Snail.Web.Template.Messaging;
#endif
using Snail.Web.Template.Proxy;
using Snail.Web.Template.RateLimiting;
#if (mongo)
using Snail.Web.Template.Storage;
#endif

namespace Snail.Web.Template.Extensions;

/// <summary>The features of the application, each registered and mapped by an extension of its own.</summary>
internal static class ApplicationRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>The proxy in front of the application, the limit per client, its health and its features.</summary>
        public IServiceCollection AddApplication(IConfiguration configuration) =>
            services
                .AddProxyHeaders(configuration)
                .AddClientRateLimit(configuration)
#if (localization)
                .AddLanguages()
#endif
#if (UsesAuth)
                .AddAccount(configuration)
#endif
#if (mongo)
                .AddStorage(configuration)
#endif
#if (rabbit)
                .AddMessaging(configuration)
#endif
#if (IsBlazor)
                .AddPages()
#endif
                .AddHealth();
    }

    extension(WebApplication app)
    {
#if (localization)
        /// <summary>
        /// The middleware in its order: the proxy's headers first, so every later step sees the client's address, then the
        /// language, so every later step answers in it.
        /// </summary>
#else
        /// <summary>The middleware in its order: the proxy's headers first, so every later step sees the client's address.</summary>
#endif
        public WebApplication UseApplication()
        {
            app.UseForwardedHeaders();
#if (localization)
            app.UseLanguages();
#endif
#if (IsBlazor)
            app.UsePageErrors();
#endif
#if (UsesAuth)
            app.UseAuthentication();
            app.UseAuthorization();
#endif
            app.UseRateLimiter();
#if (IsBlazor)
            app.UseAntiforgery();
#endif

            return app;
        }

        /// <summary>Every endpoint of the application.</summary>
        public WebApplication MapApplication()
        {
            app.MapHealth();
#if (IsApi)
            app.MapGreetings();
#endif
#if (IsBlazor)
            app.MapPages();
#if (localization)
            app.MapLanguageChoice();
#endif
#endif
#if (UsesAuth)
            app.MapAccount();
#endif

            return app;
        }
    }
}

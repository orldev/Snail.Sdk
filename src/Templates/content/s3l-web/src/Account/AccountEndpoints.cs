using System.Security.Claims;
#if (UsesCookie)
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
#endif
#if (UsesJwt)
using Snail.Toolkit.Authentication.JwtBearer;
#endif

namespace Snail.Web.Template.Account;

/// <summary>Who the user is: the sign-in scheme, the authorization the endpoints ask, and the user's own account.</summary>
internal static class AccountEndpoints
{
#if (UsesCookie)
    /// <summary>The section that says where the keys protecting the cookie are kept.</summary>
    public const string KeysSection = "DataProtection";

#endif
    extension(IServiceCollection services)
    {
#if (UsesCookie)
        /// <summary>
        /// A cookie, sliding over twelve hours, secure whenever the request came over HTTPS — behind traefik, as the
        /// proxy's X-Forwarded-Proto says. Its keys are kept in DataProtection:KeysPath, a volume, so a new container
        /// still reads the cookies the last one wrote.
        /// </summary>
        public IServiceCollection AddAccount(IConfiguration configuration)
        {
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(Configure);
            services.AddAuthorization();

            var protection = services.AddDataProtection().SetApplicationName("Snail.Web.Template");
            var keys = configuration[$"{KeysSection}:KeysPath"];

            if (!string.IsNullOrEmpty(keys))
                protection.PersistKeysToFileSystem(new DirectoryInfo(keys));

            return services;
        }
#elif (UsesJwt)
        /// <summary>A bearer token, its issuer, audience and key read from the Jwt section and checked at start.</summary>
        public IServiceCollection AddAccount(IConfiguration configuration)
        {
            services.AddAuthJwtBearer(configuration);
            services.AddAuthorization();

            return services;
        }
#endif
    }

    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>GET /account/me: the signed-in user's name, and 401 to anyone else.</summary>
        public IEndpointRouteBuilder MapAccount()
        {
            endpoints.MapGet("/account/me", (ClaimsPrincipal user) => TypedResults.Ok(new AccountView(user.Identity?.Name))).RequireAuthorization();

            return endpoints;
        }
    }
#if (UsesCookie)

    private static void Configure(CookieAuthenticationOptions cookie)
    {
        cookie.SlidingExpiration = true;
        cookie.ExpireTimeSpan = TimeSpan.FromHours(12);
        cookie.Cookie.HttpOnly = true;
        cookie.Cookie.SameSite = SameSiteMode.Lax;
        cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        cookie.Events.OnRedirectToLogin = context => Refuse(context, StatusCodes.Status401Unauthorized);
        cookie.Events.OnRedirectToAccessDenied = context => Refuse(context, StatusCodes.Status403Forbidden);
    }

    private static Task Refuse(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;

        return Task.CompletedTask;
    }
#endif
}

/// <summary>The account as the user sees it.</summary>
internal sealed record AccountView(string? Name);

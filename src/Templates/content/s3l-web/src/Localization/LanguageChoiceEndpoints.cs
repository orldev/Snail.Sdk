using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Localization;
using Snail.Web.Template.RateLimiting;

namespace Snail.Web.Template.Localization;

/// <summary>The person's choice of language, kept in the cookie the request localization reads first.</summary>
internal static class LanguageChoiceEndpoints
{
    /// <summary>How long a choice is kept: a year, renewed by every new choice.</summary>
    private static readonly TimeSpan Kept = TimeSpan.FromDays(365);

    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// GET /culture/{language}?returnUrl=…: keeps a language the application speaks and goes back to the page it was
        /// chosen on. A language it does not speak changes nothing, and an address off the site leads home instead.
        /// </summary>
        public IEndpointRouteBuilder MapLanguageChoice()
        {
            endpoints.MapGet("/culture/{language}", Choose).RequireRateLimiting(ClientRateLimit.PolicyName);

            return endpoints;
        }
    }

    private static RedirectHttpResult Choose(string language, string? returnUrl, HttpContext context)
    {
        string back = RedirectHttpResult.IsLocalUrl(returnUrl) ? returnUrl! : "/";

        if (!LanguageRegistration.Languages.Contains(language, StringComparer.Ordinal))
            return TypedResults.LocalRedirect(back);

        var cookie = new CookieOptions
        {
            MaxAge = Kept,
            IsEssential = true,
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax
        };

        context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName, CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(language)), cookie);

        return TypedResults.LocalRedirect(back);
    }
}

#if (IsBlazor)
using System.Text.Encodings.Web;
using System.Text.Unicode;
#endif
using Microsoft.AspNetCore.Localization;

namespace Snail.Web.Template.Localization;

/// <summary>
/// The language of each request: the one the person chose, kept in a cookie, then the first of the browser's
/// Accept-Language the application speaks, then English.
/// </summary>
internal static class LanguageRegistration
{
    /// <summary>
    /// The languages the application speaks: English, which answers for every language without words of its own, then
    /// each language a <c>Resources/Messages.&lt;language&gt;.resx</c> was built into, in the order of their names.
    /// </summary>
    /// <remarks>
    /// Read from the satellite assemblies beside the application rather than written in code, so a language is added by
    /// adding its file and nothing else.
    /// </remarks>
    public static IReadOnlyList<string> Languages { get; } = Spoken();

    extension(IServiceCollection services)
    {
        /// <summary>The languages a request may ask for, and the two places it asks: the cookie, then the header.</summary>
        /// <remarks>
        /// The query string, which ASP.NET asks first by default, is left out: a language passed in a link would stick to
        /// every page the link is shared to.
#if (IsBlazor)
        /// The pages write every script as it is: the default encoder lets Basic Latin alone through and writes the rest as
        /// character references, six to eight bytes for each letter of a Russian sentence. Markup is escaped either way.
#endif
        /// </remarks>
        public IServiceCollection AddLanguages()
        {
            string[] languages = [.. Languages];

            services.Configure<RequestLocalizationOptions>(options =>
            {
                options.SetDefaultCulture(Languages[0]).AddSupportedCultures(languages).AddSupportedUICultures(languages);
                options.RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
            });
#if (IsBlazor)
            services.AddWebEncoders(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
#endif

            return services;
        }
    }

    private static string[] Spoken()
    {
        var satellite = $"{typeof(Messages).Assembly.GetName().Name}.resources.dll";
        var folders = Directory.EnumerateDirectories(AppContext.BaseDirectory).Where(folder => File.Exists(Path.Combine(folder, satellite)));

        return ["en", .. folders.Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
    }

    extension(WebApplication app)
    {
        /// <summary>Sets the culture of every request before anything reads a sentence.</summary>
        public WebApplication UseLanguages()
        {
            app.UseRequestLocalization();

            return app;
        }
    }
}

using System.Globalization;
using System.Resources;

namespace Snail.Web.Template.Localization;

/// <summary>Every sentence a person reads, in one place.</summary>
/// <remarks>
/// The words live in <c>Resources/Messages.resx</c> — English, which also answers for every language the application
/// does not speak — and in a <c>Messages.&lt;language&gt;.resx</c> beside it for every other language, under keys named
/// after the group and the member here. A member reads its key in the current UI culture each time it is asked, so it is
/// a property rather than a constant: a constant would freeze one language into every call site at compile time. The
/// conformance check keeps every translation saying what the English file says (S3LC0031).
/// </remarks>
internal static class Messages
{
    private static readonly ResourceManager Texts = new("Snail.Web.Template.Resources.Messages", typeof(Messages).Assembly);

#if (IsApi)
    /// <summary>The greetings the API answers.</summary>
    internal static class Greetings
    {
        public static string Hello => Text("Greetings.Hello");
    }
#else
    /// <summary>The home page.</summary>
    internal static class Home
    {
        public static string Lead => Text("Home.Lead");
    }

    /// <summary>The page of an address that holds nothing.</summary>
    internal static class NotFound
    {
        public static string Title => Text("NotFound.Title");

        public static string Lead => Text("NotFound.Lead");
    }

    /// <summary>The page of a request that failed.</summary>
    internal static class Error
    {
        public static string Title => Text("Error.Title");

        public static string Heading => Text("Error.Heading");

        public static string Lead => Text("Error.Lead");
    }

    /// <summary>What every page shares.</summary>
    internal static class Layout
    {
        public static string Languages => Text("Layout.Languages");
    }
#endif

    /// <summary>Fills a sentence's placeholders in the current culture.</summary>
    /// <remarks>
    /// The one place a format string is used: word order differs between languages, so a sentence with a value in it is a
    /// numbered pattern in the resx rather than pieces glued together in code.
    /// </remarks>
    internal static string Format(string pattern, params object[] values) => string.Format(CultureInfo.CurrentCulture, pattern, values);

    /// <summary>Reads a sentence in the current UI culture; a key no file holds reads as itself, so a gap shows on screen.</summary>
    private static string Text(string key) => Texts.GetString(key, CultureInfo.CurrentUICulture) ?? key;
}

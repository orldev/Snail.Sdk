#if (localization)
using Snail.Web.Template.Localization;

#endif
namespace Snail.Web.Template.Greetings;

/// <summary>A greeting for someone.</summary>
internal sealed record Greeting(string Text)
{
#if (localization)
    /// <summary>The greeting for a name, in the language of the request.</summary>
    public static Greeting For(string name) => new(Messages.Format(Messages.Greetings.Hello, name));
#else
    /// <summary>The greeting for a name.</summary>
    public static Greeting For(string name) => new($"Hello, {name}!");
#endif
}

namespace Snail.Web.Template.Greetings;

/// <summary>A greeting for someone.</summary>
internal sealed record Greeting(string Text)
{
    /// <summary>The greeting for a name.</summary>
    public static Greeting For(string name) => new($"Hello, {name}!");
}

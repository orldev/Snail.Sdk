namespace Snail.Toolkit.Template;

/// <summary>What the library says to someone: the sample a new library starts from, to be replaced by its own types.</summary>
public static class Greeting
{
    /// <summary>The greeting for a name.</summary>
    public static string For(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return $"Hello, {name}";
    }
}

using Snail.Sdk.Docs;

namespace Snail.Sdk.Sdk.Tests;

/// <summary>A summary that names a generic type or method reads as C# writes it, not as the id the compiler stores.</summary>
public sealed class DocumentationTests : IDisposable
{
    private const string Xml = """
        <?xml version="1.0"?>
        <doc>
          <members>
            <member name="T:Library.Reader">
              <summary>Answers an <see cref="T:Library.Answer`1"/> through <see cref="M:Library.ToolAnswer.Ok``1(``0)"/>, or a <see cref="T:Library.Pair`2"/>.</summary>
            </member>
          </members>
        </doc>
        """;

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"documentation-{Guid.NewGuid():N}.xml");

    [Fact]
    public void Summary_NamingGenericTypesAndMethods_WritesThemAsCSharpDoes()
    {
        File.WriteAllText(_file, Xml);

        var summary = Documentation.Read(_file).Summary("T:Library.Reader");

        Assert.Contains("`Answer<T>`", summary, StringComparison.Ordinal);
        Assert.Contains("`Ok<T>`", summary, StringComparison.Ordinal);
        Assert.Contains("`Pair<T1, T2>`", summary, StringComparison.Ordinal);
    }

    public void Dispose() => File.Delete(_file);
}

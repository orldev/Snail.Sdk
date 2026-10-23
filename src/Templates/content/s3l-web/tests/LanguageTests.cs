using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Snail.Web.Template.Localization;

namespace Snail.Web.Template.Tests;

/// <summary>The application speaks the languages of its resource files; the conformance check keeps them alike (S3LC0031).</summary>
public sealed class LanguageTests
{
    private static readonly ResourceManager Texts = new("Snail.Web.Template.Resources.Messages", typeof(Messages).Assembly);

    private static IEnumerable<string> Keys() =>
        typeof(Messages).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .SelectMany(group => group.GetProperties(BindingFlags.Public | BindingFlags.Static).Select(member => $"{group.Name}.{member.Name}"));

    /// <summary>
    /// A language is spoken because its file is there: English first, then each Messages.&lt;language&gt;.resx, so a new
    /// file adds a language without a change here.
    /// </summary>
    [Fact]
    public void Languages_BeyondEnglish_EachHaveSentencesOfTheirOwn()
    {
        var translated = LanguageRegistration.Languages.Skip(1);
        var silent = translated.Where(language => Texts.GetResourceSet(CultureInfo.GetCultureInfo(language), createIfNotExists: true, tryParents: false) is null);

        Assert.Equal("en", LanguageRegistration.Languages[0]);
        Assert.NotEmpty(translated);
        Assert.Empty(silent);
    }

    /// <summary>A member whose key no file holds would show the key itself on screen, and a key no member reads is dead weight.</summary>
    [Fact]
    public void Messages_EveryMember_ReadsAKeyTheResourcesHold()
    {
        var english = Texts.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
        var keys = english.Cast<DictionaryEntry>().Select(entry => (string)entry.Key);

        Assert.Equal(keys.Order(StringComparer.Ordinal), Keys().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("en-US", "en")]
    [InlineData("ru-RU", "ru")]
    [InlineData("es-MX", "es")]
    [InlineData("de-DE", "en")]
    public void Messages_InAUICulture_ReadThatLanguageOrEnglish(string culture, string expected)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        try
        {
#if (IsApi)
            Assert.Equal(Texts.GetString("Greetings.Hello", CultureInfo.GetCultureInfo(expected)), Messages.Greetings.Hello);
#else
            Assert.Equal(Texts.GetString("NotFound.Lead", CultureInfo.GetCultureInfo(expected)), Messages.NotFound.Lead);
#endif
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}

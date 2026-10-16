using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Snail.Sdk.Analyzers.Tests;

public sealed class RulesTests
{
    private static readonly DiagnosticDescriptor[] Descriptors =
    [
        .. typeof(Rules).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.GetValue(null)).OfType<DiagnosticDescriptor>()
    ];

    [Fact]
    public void EveryRule_LinksToItsOwnPage()
    {
        Assert.NotEmpty(Descriptors);

        Assert.All(Descriptors, rule => Assert.Equal($"https://orldev.github.io/Snail.Sdk/rules/{rule.Id}/", rule.HelpLinkUri));
    }

    [Fact]
    public void EveryId_HasFourDigits()
    {
        var suppressions = new Released().SupportedSuppressions.Select(suppression => suppression.Id);
        var ids = Descriptors.Select(rule => rule.Id).Concat(suppressions);

        Assert.All(ids, id => Assert.Matches("^S3LS?[0-9]{4}$", id));
    }
}

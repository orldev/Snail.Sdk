using Microsoft.Extensions.Options;

namespace Snail.Web.Template.Proxy;

/// <summary>Refuses to start with a proxy network that is not one: a typo would silently trust nobody.</summary>
internal sealed class ProxyOptionsValidator : IValidateOptions<ProxyOptions>
{
    /// <summary>Whether every network of the options reads as one.</summary>
    public ValidateOptionsResult Validate(string? name, ProxyOptions options)
    {
        var malformed = ProxyNetworks.Malformed(options.Networks);

        return malformed.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{ProxyOptions.SectionName}:Networks holds what is not a network in CIDR form: {string.Join(", ", malformed)}");
    }
}

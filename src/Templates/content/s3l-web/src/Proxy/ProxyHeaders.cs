using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Snail.Web.Template.Proxy;

/// <summary>The client's address and scheme as the proxy in front of the application saw them.</summary>
internal static class ProxyHeaders
{
    extension(IServiceCollection services)
    {
        /// <summary>X-Forwarded-For and X-Forwarded-Proto, believed from the configured networks only and checked at start.</summary>
        public IServiceCollection AddProxyHeaders(IConfiguration configuration)
        {
            services.AddOptions<ProxyOptions>().Bind(configuration.GetSection(ProxyOptions.SectionName)).ValidateOnStart();
            services.AddSingleton<IValidateOptions<ProxyOptions>, ProxyOptionsValidator>();
            services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ProxyOptions>>(Trust);

            return services;
        }
    }

    private static void Trust(ForwardedHeadersOptions forwarded, IOptions<ProxyOptions> proxy)
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();

        foreach (var network in ProxyNetworks.Parse(proxy.Value.Networks))
            forwarded.KnownIPNetworks.Add(network);
    }
}

using Snail.Toolkit.HashiCorp.Vault;

namespace Snail.Web.Template.Extensions;

/// <summary>Secrets from HashiCorp Vault, read into the configuration when it names a Vault.</summary>
internal static class VaultConfiguration
{
    /// <summary>The section that names the Vault, its credentials and the secrets to read.</summary>
    public const string SectionName = "Vault";

    extension(IConfigurationManager configuration)
    {
        /// <summary>
        /// Adds Vault when the configuration has a Vault section: an application started on a developer's machine or
        /// in its tests reads appsettings and variables alone.
        /// </summary>
        public IConfigurationManager AddVaultWhenConfigured()
        {
            if (configuration.GetSection(SectionName).Exists())
                configuration.AddVault();

            return configuration;
        }
    }
}

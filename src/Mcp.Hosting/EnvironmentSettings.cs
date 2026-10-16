using System.Collections;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>Maps <c>PREFIX_SECTION__FIELD_NAME</c> variables onto configuration keys <c>Section:FieldName</c>.</summary>
/// <remarks>
/// The variables are read under one prefix and nothing else, so an unrelated variable of whoever started the server
/// can never become a setting of it by sharing a name: <c>SNAIL_MCP_CLEANUP_DATA_DIRECTORY</c> is <c>DataDirectory</c>
/// to Snail.MCP.CleanUp and nothing to any other server.
/// </remarks>
public static class EnvironmentSettings
{
    /// <summary>The variables under the prefix as configuration keys; a blank one, and one whose name is only the prefix or leaves a section empty, left out.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> Read(string prefix, IDictionary variables)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentNullException.ThrowIfNull(variables);

        foreach (DictionaryEntry entry in variables)
        {
            if (entry.Key is not string name || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.Value is not string value || string.IsNullOrWhiteSpace(value))
                continue;

            var key = ToConfigurationKey(name[prefix.Length..]);

            if (!IsKey(key))
                continue;

            yield return new KeyValuePair<string, string?>(key, value.Trim());
        }
    }

    /// <summary>A variable's name after the prefix as a configuration key: double underscores nest, single ones vanish.</summary>
    public static string ToConfigurationKey(string variable) =>
        string.Join(':', variable.Split("__").Select(segment => segment.Replace("_", string.Empty, StringComparison.Ordinal)));

    /// <summary>Whether every section of the key has a name: <c>SNAIL_MCP_DEMO_</c> and <c>SNAIL_MCP_DEMO___PORT</c> name none.</summary>
    private static bool IsKey(string key) => key.Length > 0 && !key.Split(':').Any(section => section.Length == 0);
}

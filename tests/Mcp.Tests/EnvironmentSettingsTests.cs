using System.Collections;

namespace Snail.Sdk.Mcp.Tests;

public sealed class EnvironmentSettingsTests
{
    [Fact]
    public void Variables_UnderThePrefix_BecomeConfigurationKeys_TheRestAndTheBlankAreLeftOut()
    {
        IDictionary variables = new Hashtable
        {
            ["SNAIL_MCP_DEMO_DATA_DIRECTORY"] = " /data ",
            ["SNAIL_MCP_DEMO_SECTION__FIELD_NAME"] = "nested",
            ["SNAIL_MCP_DEMO_EMPTY"] = " ",
            ["HOME"] = "/Users/me"
        };

        var read = EnvironmentSettings.Read("SNAIL_MCP_DEMO_", variables).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

        Assert.Equal([new KeyValuePair<string, string?>("DATADIRECTORY", "/data"), new KeyValuePair<string, string?>("SECTION:FIELDNAME", "nested")], read);
    }

    /// <summary>A name that is only the prefix, or leaves a section empty with three underscores, names no setting: neither becomes a key.</summary>
    [Fact]
    public void Variables_ThatAreOnlyThePrefix_OrLeaveASectionEmpty_AreLeftOut()
    {
        IDictionary variables = new Hashtable { ["SNAIL_MCP_DEMO_"] = "x", ["SNAIL_MCP_DEMO___PORT"] = "1", ["SNAIL_MCP_DEMO_PORT"] = "2" };

        var read = EnvironmentSettings.Read("SNAIL_MCP_DEMO_", variables).ToList();

        Assert.Equal(["PORT"], read.Select(setting => setting.Key));
    }
}

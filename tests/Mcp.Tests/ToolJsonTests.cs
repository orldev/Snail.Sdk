using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.Sdk.Mcp.Tests;

public sealed partial class ToolJsonTests
{
    [Fact]
    public void Options_ResolveTheServersTypes_AndTheSdksOwn()
    {
        var options = ToolJson.Options(DemoJson.Default);

        Assert.NotNull(options.GetTypeInfo(typeof(Snail.Sdk.Mcp.Hosting.Jobs.JobReport)));

        var report = new Snail.Sdk.Mcp.Hosting.Jobs.JobReport("job://x/y/1", Snail.Sdk.Mcp.Hosting.Jobs.JobState.Succeeded, DateTimeOffset.UnixEpoch, null, null, null, null);

        Assert.Contains("\"state\":\"succeeded\"", JsonSerializer.Serialize(report, options), StringComparison.Ordinal);

        Assert.True(options.IsReadOnly);
        Assert.Equal("""{"side":"left"}""", JsonSerializer.Serialize(new Arrow { Side = "left" }, options));
        Assert.NotNull(options.GetTypeInfo(typeof(ModelContextProtocol.Protocol.CallToolResult)));
    }

    /// <summary>The trap the remarks of ToolJson name: a default on a set property survives a field left out.</summary>
    [Fact]
    public void Default_OfASetProperty_SurvivesAFieldLeftOut() =>
        Assert.Equal("auto", JsonSerializer.Deserialize("{}", DemoJson.Default.Arrow)!.Side);

    public sealed record Arrow
    {
        public string Side { get; set; } = "auto";
    }

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(Arrow))]
    internal sealed partial class DemoJson : JsonSerializerContext;
}

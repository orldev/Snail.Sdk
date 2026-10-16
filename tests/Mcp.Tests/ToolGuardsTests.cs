using System.Text.Json;
using ModelContextProtocol.Protocol;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

public sealed class ToolGuardsTests
{
    private static readonly JsonElement DescribedParameters = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "scope": { "type": "string", "enum": ["mine", "everything"], "description": "mine or everything." }
          }
        }
        """).RootElement;

    [Fact]
    public void Problems_OfAToolKeepingEveryRule_AreNone()
    {
        Tool[] tools = [Tool("cleanup_plan", "Decide a cleanup.", DescribedParameters, readOnly: true)];

        Assert.Empty(ToolGuards.Problems(tools));
    }

    [Fact]
    public void Problems_NameEachBrokenRule()
    {
        var parameters = JsonDocument.Parse("""
            { "type": "object", "properties": { "scope": { "type": "string", "enum": ["mine", "orphans"] } } }
            """).RootElement;
        Tool[] tools =
        [
            Tool("CleanupPlan", "Решить.", parameters, readOnly: null),
            Tool("CleanupPlan", string.Empty, DescribedParameters, readOnly: false)
        ];

        var problems = ToolGuards.Problems(tools);

        Assert.Contains("CleanupPlan: listed more than once", problems);
        Assert.Contains("CleanupPlan: the name is not snake case", problems);
        Assert.Contains("CleanupPlan: the description is not in English", problems);
        Assert.Contains("CleanupPlan: no description", problems);
        Assert.Contains("CleanupPlan: does not say whether it changes anything (ReadOnly or Destructive)", problems);
        Assert.Contains("CleanupPlan.scope: no description", problems);
        Assert.Contains("CleanupPlan.scope: the descriptions never name mine, orphans", problems);
    }

    /// <summary>A value hidden inside a longer word is not named: "all" in "allows" tells the model nothing.</summary>
    [Fact]
    public void Problems_OfAValueOnlyInsideALongerWord_NameIt()
    {
        var parameters = JsonDocument.Parse("""
            { "type": "object", "properties": { "scope": { "type": "string", "enum": ["all", "mine"], "description": "Allows mine-only or everything." } } }
            """).RootElement;
        Tool[] tools = [Tool("cleanup_plan", "Decide a cleanup.", parameters, readOnly: true)];

        Assert.Equal(["cleanup_plan.scope: the descriptions never name all, mine"], ToolGuards.Problems(tools));
    }

    /// <summary>An array of allowed values hides them under items; the model still has to read each one.</summary>
    [Fact]
    public void Problems_OfAnArrayParameter_CheckTheValuesOfItsItems()
    {
        var parameters = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "kinds": { "type": "array", "items": { "type": "string", "enum": ["process", "port"] }, "description": "Each a process." }
              }
            }
            """).RootElement;
        Tool[] tools = [Tool("cleanup_plan", "Decide a cleanup.", parameters, readOnly: true)];

        Assert.Equal(["cleanup_plan.kinds: the descriptions never name port"], ToolGuards.Problems(tools));
    }

    /// <summary>A value is named however the text around it is punctuated: quoted, in a list, at the end of a sentence.</summary>
    [Fact]
    public void Problems_OfValuesNamedAsWords_AreNone()
    {
        var parameters = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "kinds": { "type": "array", "items": { "enum": ["process", "dry_run", "read-only"] }, "description": "'process', dry_run or Read-Only." }
              }
            }
            """).RootElement;
        Tool[] tools = [Tool("cleanup_plan", "Decide a cleanup.", parameters, readOnly: true)];

        Assert.Empty(ToolGuards.Problems(tools));
    }

    /// <summary>A schema written by hand with its properties as a list: what cannot be read is a problem the list names, never an exception that hides every other problem.</summary>
    [Fact]
    public void Problems_OfASchemaWhosePropertiesIsAList_NameTheTool()
    {
        var schema = JsonDocument.Parse("""{ "type": "object", "properties": [] }""").RootElement;
        Tool[] tools = [Tool("x_list", "Lists.", schema, readOnly: true)];

        Assert.Equal(["x_list: properties is Array, not an object of parameters"], ToolGuards.Problems(tools));
    }

    private static Tool Tool(string name, string description, JsonElement parameters, bool? readOnly) => new()
    {
        Name = name,
        Description = description,
        InputSchema = parameters,
        Annotations = readOnly is null ? null : new ToolAnnotations { ReadOnlyHint = readOnly }
    };
}

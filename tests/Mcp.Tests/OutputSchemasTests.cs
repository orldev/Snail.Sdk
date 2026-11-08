using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>Whether an answer fits the output schema its tool publishes, checked the way a validating client checks it.</summary>
public sealed class OutputSchemasTests
{
    private static readonly Tool Sized = Schema("""
        {"type":"object","properties":{
          "size":{"type":"integer"},
          "kind":{"enum":["file","folder"]},
          "tags":{"type":"array","items":{"type":"string"}},
          "owner":{"anyOf":[{"type":"string"},{"type":"null"}]},
          "child":{"$ref":"#/$defs/node"}},
         "required":["size"],
         "additionalProperties":false,
         "$defs":{"node":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}}}
        """);

    [Fact]
    public void Answer_ThatFitsAnEnvelopeSchema_HasNoProblems()
    {
        var tool = EnvelopeTool();

        var answer = ToolAnswer.Ok(new JsonObject { ["path"] = "a.txt", ["lines"] = 3 }, AnswerShape.Envelope);

        Assert.Empty(OutputSchemas.Problems(tool, answer));
    }

    /// <summary>The envelope's schema says a success has its data: null for it is refused, as a validating client refuses it.</summary>
    [Fact]
    public void Answer_WhoseEnvelopeHasNullData_IsAProblem()
    {
        var tool = EnvelopeTool();

        var answer = new CallToolResult { Content = [new TextContentBlock { Text = "{}" }], StructuredContent = JsonElement.Parse("""{"ok":true,"data":null}""") };

        Assert.Equal(["$.data: is null, the schema takes object"], OutputSchemas.Problems(tool, answer));
    }

    [Fact]
    public void Answer_OfAValueOfTheWrongKind_NamesItsPlace()
    {
        var tool = EnvelopeTool();

        var answer = ToolAnswer.Ok(new JsonObject { ["path"] = "a.txt", ["lines"] = "three" }, AnswerShape.Envelope);

        Assert.Equal(["$.data.lines: is a string, the schema takes integer"], OutputSchemas.Problems(tool, answer));
    }

    [Fact]
    public void Success_WithoutStructuredContent_IsAProblem_ForAToolWithASchema()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["size"] = 1 }, AnswerShape.None);

        var problem = Assert.Single(OutputSchemas.Problems(Sized, answer));
        Assert.Contains("carries no structured content", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_WithoutStructuredContent_HasNoProblems() =>
        Assert.Empty(OutputSchemas.Problems(Sized, ToolAnswer.Failed("No.")));

    [Fact]
    public void Tool_WithoutASchema_HasNothingToFit() =>
        Assert.Empty(OutputSchemas.Problems(new Tool { Name = "plain" }, ToolAnswer.Ok(new JsonObject { ["a"] = 1 })));

    [Fact]
    public void Answer_ThatFitsEveryKeyword_HasNoProblems()
    {
        var answer = ToolAnswer.Ok(JsonNode.Parse("""{"size":2.0,"kind":"file","tags":["a"],"owner":null,"child":{"name":"x"}}""")!);

        Assert.Empty(OutputSchemas.Problems(Sized, answer));
    }

    [Fact]
    public void Answer_ThatBreaksEveryKeyword_HasAProblemForEach()
    {
        var answer = ToolAnswer.Ok(JsonNode.Parse("""{"kind":"link","tags":["a",1],"owner":3,"child":{},"extra":true}""")!);

        Assert.Equal(
            [
                "$: lacks 'size', which the schema requires",
                """$.kind: is "link", the schema takes one of ["file","folder"]""",
                "$.tags[1]: is the number 1, the schema takes string",
                "$.owner: is 3, which fits none of the schema's anyOf",
                "$.child: lacks 'name', which the schema requires",
                "$.extra: the schema allows no property of this name",
            ],
            OutputSchemas.Problems(Sized, answer));
    }

    [Fact]
    public void Number_WithAFraction_IsNoInteger()
    {
        var answer = ToolAnswer.Ok(new JsonObject { ["size"] = 1.5 });

        Assert.Equal(["$.size: is the number 1.5, the schema takes integer"], OutputSchemas.Problems(Sized, answer));
    }

    private static Tool Schema(string schema) => new() { Name = "sized", OutputSchema = JsonElement.Parse(schema) };

    private static Tool EnvelopeTool()
    {
        var method = typeof(ToolAnswerTests).GetMethod("ReadFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        return McpServerTool.Create(method, (object?)null, new McpServerToolCreateOptions { SerializerOptions = ToolJson.Options(EnvelopeJson.Default) }).ProtocolTool;
    }
}

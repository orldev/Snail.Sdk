namespace Snail.Sdk.Analyzers.Tests;

/// <summary>S3L0011: a tool answers in the one envelope, and what the rule cannot see it leaves alone.</summary>
public sealed class EnvelopeTests
{
    /// <summary>The MCP SDK's and Snail.Sdk's types as far as the rule looks at them.</summary>
    private const string Stubs = """

        namespace ModelContextProtocol.Server
        {
            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class McpServerToolAttribute : System.Attribute { public string? Name { get; set; } }
        }

        namespace ModelContextProtocol.Protocol
        {
            public sealed class CallToolResult { public bool IsError { get; set; } }
            public sealed class TextContentBlock { public string Text { get; set; } = ""; }
        }

        namespace Snail.Sdk.Mcp.Hosting.Answers
        {
            public static class ToolAnswer
            {
                public static ModelContextProtocol.Protocol.CallToolResult Ok(object? data) => new();
                public static string OkText(object? data) => "";
                public static string FailedText(string error, string? hint = null) => "";
            }

            public sealed record Envelope<T>(bool Ok, T Data);
        }
        """;

    [Fact]
    public Task Tools_ThatAnswerWhatToolAnswerBuilds_AreLeftAlone() => Verify<OneEnvelope>.AnalyzerAsync("""
        using System.Threading.Tasks;
        using ModelContextProtocol.Protocol;
        using ModelContextProtocol.Server;
        using Snail.Sdk.Mcp.Hosting.Answers;

        public sealed class Tools
        {
            [McpServerTool(Name = "a")] public CallToolResult A() => ToolAnswer.Ok(1);
            [McpServerTool(Name = "b")] public async Task<CallToolResult> BAsync() { await Task.Yield(); return ToolAnswer.Ok(2); }
            [McpServerTool(Name = "c")] public ValueTask<CallToolResult> C() => new(ToolAnswer.Ok(3));
            [McpServerTool(Name = "d")] public string D(bool isOk) => isOk ? ToolAnswer.OkText(4) : ToolAnswer.FailedText("no", "try again");
            [McpServerTool(Name = "e")] public Task<string> EAsync() => Task.FromResult(ToolAnswer.OkText(5));
            [McpServerTool(Name = "f")] public Envelope<int> F() => new(true, 6);
        }
        """ + Stubs);

    /// <summary>Snail.MCP.Figma's string tools hand their answer over from a method of their own: the rule cannot see it, and does not guess.</summary>
    [Fact]
    public Task Tools_WhoseAnswerTheRuleCannotSee_AreLeftAlone() => Verify<OneEnvelope>.AnalyzerAsync("""
        using System;
        using System.Threading.Tasks;
        using ModelContextProtocol.Server;

        public sealed class Tools
        {
            [McpServerTool(Name = "a")] public Task<string> AAsync() => ExecuteAsync(() => "a script");
            [McpServerTool(Name = "b")] public string B(string text) { var answer = text; return answer; }
            [McpServerTool(Name = "c")] public object C() => "anything";
            [McpServerTool(Name = "d")] public T D<T>(T value) => value;
            [McpServerTool(Name = "e")] public string E(string text) => text.ToUpperInvariant();
            [McpServerTool(Name = "f")] public string F() { Func<string> later = () => "inside a lambda"; return Of(later()); }

            private static Task<string> ExecuteAsync(Func<string> script) => Task.FromResult(script());
            private static string Of(string text) => text;
        }
        """ + Stubs);

    [Fact]
    public Task Tools_ThatAnswerPlainText_AreReported() => Verify<OneEnvelope>.AnalyzerAsync("""
        using System.Text;
        using System.Threading.Tasks;
        using ModelContextProtocol.Server;
        using System.Text.Json.Nodes;

        public sealed class Tools
        {
            [McpServerTool(Name = "a")] public string A() => {|S3L0011:"done"|};
            [McpServerTool(Name = "b")] public string B(int count) => {|S3L0011:$"{count} files"|};
            [McpServerTool(Name = "c")] public string C(string name) => {|S3L0011:"hello " + name|};
            [McpServerTool(Name = "d")] public string D(string? text) => text ?? {|S3L0011:string.Empty|};
            [McpServerTool(Name = "e")] public string E(int[] items) => {|S3L0011:string.Join(", ", items)|};
            [McpServerTool(Name = "f")] public string F(StringBuilder lines) => {|S3L0011:lines.ToString()|};
            [McpServerTool(Name = "g")] public Task<string> GAsync() => Task.FromResult({|S3L0011:"later"|});
            [McpServerTool(Name = "h")] public string H(JsonObject report) { report = new JsonObject(); return {|S3L0011:report.ToJsonString()|}; }
            [McpServerTool(Name = "i")] public string I(bool isOk) => isOk ? Snail.Sdk.Mcp.Hosting.Answers.ToolAnswer.OkText(1) : {|S3L0011:"failed"|};
            [McpServerTool(Name = "j")] public async Task<string> JAsync(int code) { await Task.Yield(); return code switch { 0 => {|S3L0011:"ok"|}, _ => {|S3L0011:$"code {code}"|} }; }
        }
        """ + Stubs);

    [Fact]
    public Task Tools_ThatAnswerAResultBuiltByHand_AreReported() => Verify<OneEnvelope>.AnalyzerAsync("""
        using System.Threading.Tasks;
        using ModelContextProtocol.Protocol;
        using ModelContextProtocol.Server;

        public sealed class Tools
        {
            [McpServerTool(Name = "a")] public CallToolResult A() => {|S3L0011:new CallToolResult { IsError = true }|};
            [McpServerTool(Name = "b")] public Task<CallToolResult> BAsync() => Task.FromResult({|S3L0011:new CallToolResult()|});
            [McpServerTool(Name = "c")] public CallToolResult C() { return {|S3L0011:new() { IsError = false }|}; }
        }
        """ + Stubs);

    /// <summary>A record, a node, a number, content blocks, or nothing at all: the MCP SDK serializes them as they are, never as the envelope.</summary>
    [Fact]
    public Task Tools_ThatAnswerAnotherType_OrNothing_AreReported() => Verify<OneEnvelope>.AnalyzerAsync("""
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using ModelContextProtocol.Protocol;
        using ModelContextProtocol.Server;
        using System.Text.Json.Nodes;

        public sealed record Report(int Count);

        public sealed class Tools
        {
            [McpServerTool(Name = "a")] public {|S3L0011:Report|} A() => new(1);
            [McpServerTool(Name = "b")] public {|S3L0011:Task<JsonNode>|} BAsync() => Task.FromResult<JsonNode>(new JsonObject());
            [McpServerTool(Name = "c")] public {|S3L0011:int|} C() => 3;
            [McpServerTool(Name = "d")] public {|S3L0011:IEnumerable<TextContentBlock>|} D() => [];
            [McpServerTool(Name = "e")] public {|S3L0011:void|} E() { }
            [McpServerTool(Name = "f")] public {|S3L0011:Task|} FAsync() => Task.CompletedTask;
        }
        """ + Stubs);

    [Fact]
    public Task Methods_ThatAreNoTools_AreLeftAlone() => Verify<OneEnvelope>.AnalyzerAsync("""
        public sealed class Tools
        {
            public string Describe() => "not a tool";
            public int Count() => 3;
        }
        """ + Stubs);

    /// <summary>A project that does not reference the MCP SDK has no tools, and the rule does nothing in it.</summary>
    [Fact]
    public Task Project_WithoutTheMcpSdk_IsLeftAlone() => Verify<OneEnvelope>.AnalyzerAsync("""
        [System.AttributeUsage(System.AttributeTargets.Method)]
        public sealed class McpServerToolAttribute : System.Attribute { }

        public sealed class Tools
        {
            [McpServerTool] public string Say() => "plain";
        }
        """);

    [Fact]
    public Task Message_NamesTheToolAndWhatItAnswers() => Verify<OneEnvelope>.AnalyzerAsync("""
        using ModelContextProtocol.Server;

        public sealed class Tools
        {
            [McpServerTool(Name = "say")] public string Say() => {|#0:"plain"|};
        }
        """ + Stubs, expected: [Verify<OneEnvelope>.Diagnostic("S3L0011").WithLocation(0).WithArguments("Say", "plain text")]);
}

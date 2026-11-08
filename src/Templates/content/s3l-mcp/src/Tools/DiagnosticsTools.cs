using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Template.Tools;

/// <summary>The state of the server: the sample tool a new server starts from.</summary>
/// <remarks>
/// Every tool answers through ToolAnswer: <c>{"ok":true,"data":…}</c>, or <c>{"ok":false,"error":…,"hint":…,"type":…}</c> flagged
/// isError. A failure the tool foresees is <c>ToolAnswer.Failed</c>; one it does not is an exception, which
/// <c>ExplainFailures</c> answers in the same envelope.
/// </remarks>
[McpServerToolType]
public sealed class DiagnosticsTools
{
    /// <summary>Reports the server's name, version and runtime.</summary>
    [McpServerTool(Name = "tmplshort_diagnose", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(ToolDescriptions.Diagnose)]
    public static CallToolResult Diagnose()
    {
        AssemblyName assembly = typeof(DiagnosticsTools).Assembly.GetName();
        JsonObject state = new()
        {
            ["server"] = assembly.Name,
            ["version"] = assembly.Version?.ToString(3),
            ["runtime"] = Environment.Version.ToString()
        };

        return ToolAnswer.Ok(state);
    }
}

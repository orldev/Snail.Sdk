using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace Snail.MCP.Probe;

/// <summary>The answers of the probe's paths beside its MCP endpoint.</summary>
internal static class Probe
{
    /// <summary>Ready: the path a server answers with success once it can serve.</summary>
    public static readonly RequestDelegate Healthy = _ => Task.CompletedTask;

    /// <summary>Never ready: the path of a server that keeps saying it cannot serve yet.</summary>
    public static readonly RequestDelegate Unready = context =>
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

        return Task.CompletedTask;
    };
}

/// <summary>The probe's tools: what a test asks of a session.</summary>
[McpServerToolType]
internal sealed class ProbeTools(IHttpContextAccessor requests)
{
    /// <summary>The query of the request the session was opened with.</summary>
    [McpServerTool(Name = "query", ReadOnly = true), Description("The query of the request the session was opened with.")]
    public string Query() => requests.HttpContext?.Request.QueryString.Value ?? string.Empty;
}

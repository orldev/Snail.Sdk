using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    [SuppressMessage("Snail", "S3L0011:Answer in the envelope", Justification = "A double of the tests of HttpServerProgram, which read its text as it is.")]
    public string Query() => requests.HttpContext?.Request.QueryString.Value ?? string.Empty;
}

/// <summary>What the probe asks for when it is told to ask for a token: options validated at the start.</summary>
internal sealed class ProbeOptions
{
    /// <summary>The token, from SNAIL_MCP_PROBE_TOKEN.</summary>
    public string? Token { get; set; }

    /// <summary>A wait, from SNAIL_MCP_PROBE_DELAY, bound as the host starts: a value that is no time span refuses the start.</summary>
    public TimeSpan Delay { get; set; }
}

/// <summary>What the probe binds before any host exists, as a server reads its settings to choose its transport.</summary>
internal sealed class ProbeLimits
{
    /// <summary>A count, from SNAIL_MCP_PROBE_RETRIES: a value that is no number refuses the start before the host is built.</summary>
    public int Retries { get; set; }
}

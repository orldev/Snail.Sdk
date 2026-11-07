using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Snail.Sdk.Mcp.Hosting.Answers;

/// <summary>A failure of a tool told as the envelope every answer is, and flagged as one by the protocol.</summary>
public static partial class ToolFailures
{
    /// <summary>The <c>type</c> of the answer a failure nobody foresaw becomes.</summary>
    internal const string Internal = "internal";

    /// <summary>The hint of the answer a failure nobody foresaw becomes.</summary>
    internal const string Hint = "The server failed inside, not because of the call; its log has the trace. Try once more, and if it fails the same way, report it instead of retrying.";

    extension(IMcpServerBuilder builder)
    {
        /// <summary>
        /// Answers a tool that threw with the failure envelope — its message, a hint and the type <c>internal</c> — logged
        /// once with its trace, and flags an answer that is the failure envelope but not an error as one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every failure a server expects is an answer already; this catches the rest. Without it the MCP SDK answers
        /// "An error occurred invoking …", which tells the model neither what went wrong nor whether to try again
        /// (Snail.MCP.Documents). A tool that returns the envelope as a string reaches the client without <c>isError</c>,
        /// since the SDK turns a string into a result as it is: in Snail.MCP.Figma the failures of 105 call sites read as
        /// successes until one filter flagged them all.
        /// </para>
        /// <para>
        /// The filter goes first, whenever it is called, so it sees what every other filter answers or throws. A call
        /// cancelled by its client stays cancelled, and an <see cref="McpProtocolException"/> stays the protocol error a tool
        /// threw it to be. Called twice, it registers once.
        /// </para>
        /// </remarks>
        public IMcpServerBuilder ExplainFailures()
        {
            if (builder.Services.Any(service => service.ServiceType == typeof(Explained)))
                return builder;

            builder.Services.AddSingleton(new Explained());
            builder.Services.AddOptions<McpServerOptions>().PostConfigure(options => options.Filters.Request.CallToolFilters.Insert(0, Explaining));

            return builder;
        }
    }

    private static McpRequestHandler<CallToolRequestParams, CallToolResult> Explaining(McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            CallToolResult answer;

            try
            {
                answer = await next(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsUnforeseen(failure, cancellationToken))
            {
                var loggers = request.Services?.GetService<ILoggerFactory>() ?? request.Server.Services?.GetService<ILoggerFactory>();

                if (loggers is not null)
                    Failed(loggers.CreateLogger(typeof(ToolFailures)), failure, request.Params?.Name ?? "a tool");

                return ToolAnswer.Failed(failure.Message, Hint, Internal);
            }

            if (answer.IsError != true && ToolAnswer.IsFailure(answer))
                answer.IsError = true;

            return answer;
        };

    private static bool IsUnforeseen(Exception failure, CancellationToken cancellationToken) =>
        failure is not McpProtocolException && !(failure is OperationCanceledException && cancellationToken.IsCancellationRequested);

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "{Tool} failed inside the server")]
    private static partial void Failed(ILogger logger, Exception failure, string tool);

    /// <summary>The mark that the filter is registered, so a second call adds nothing.</summary>
    private sealed class Explained;
}

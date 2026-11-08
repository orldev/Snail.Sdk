using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        /// A call whose arguments the MCP SDK could not bind never reached the tool, and is the call's fault rather than
        /// the server's: it is answered with the type <c>arguments</c>, the parameter named when the tool's input schema
        /// shows which — left out, or given a value of the wrong kind — and what the tool takes as the hint, and is not
        /// logged as a failure of the server. "Try once more" had the model repeat the call as it was.
        /// </para>
        /// <para>
        /// The filter goes first, whenever it is called, so it sees what every other filter answers or throws. A call
        /// cancelled by its client stays cancelled, and an <see cref="McpProtocolException"/> stays the protocol error a tool
        /// threw it to be. Called twice, it registers once.
        /// </para>
        /// </remarks>
        public IMcpServerBuilder ExplainFailures() => Register(builder, null);

        /// <summary>
        /// Answers a tool that threw with the failure envelope, in the words <paramref name="configure"/> gives a failure
        /// nobody foresaw: its error and its hint, the type staying <c>internal</c>.
        /// </summary>
        /// <remarks>
        /// As <c>ExplainFailures()</c>, for a server whose failures named the tool or pointed at its own log
        /// (Snail.MCP.Documents' "read_file failed: …"). Called again, the filter stays one and each
        /// <paramref name="configure"/> applies in turn.
        /// </remarks>
        public IMcpServerBuilder ExplainFailures(Action<FailureOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            return Register(builder, configure);
        }
    }

    private static IMcpServerBuilder Register(IMcpServerBuilder builder, Action<FailureOptions>? configure)
    {
        if (configure is not null)
            builder.Services.Configure(configure);

        if (builder.Services.Any(service => service.ServiceType == typeof(Explained)))
            return builder;

        builder.Services.AddSingleton(new Explained());
        builder.Services.AddOptions<McpServerOptions>().PostConfigure<IOptions<FailureOptions>>((options, failures) => options.Filters.Request.CallToolFilters.Insert(0, next => Explaining(next, failures.Value)));

        return builder;
    }

    private static McpRequestHandler<CallToolRequestParams, CallToolResult> Explaining(McpRequestHandler<CallToolRequestParams, CallToolResult> next, FailureOptions words) =>
        async (request, cancellationToken) =>
        {
            CallToolResult answer;

            try
            {
                answer = await next(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (IsUnforeseen(failure, cancellationToken))
            {
                if (ArgumentFaults.AnswerTo(request, failure) is { } refused)
                    return refused;

                var tool = request.Params?.Name ?? "a tool";
                var loggers = request.Services?.GetService<ILoggerFactory>() ?? request.Server.Services?.GetService<ILoggerFactory>();

                if (loggers is not null)
                    Failed(loggers.CreateLogger(typeof(ToolFailures)), failure, tool);

                return ToolAnswer.Failed(words.Error?.Invoke(tool, failure) ?? failure.Message, words.Hint, Internal);
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

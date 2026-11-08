using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.Sdk.Mcp.Hosting;

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
        /// A <see cref="ToolRefusalException"/> is the call's fault too, and the tool said so: it is answered in its own
        /// words — its error, hint, type and details — and not logged as a failure of the server.
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
        /// nobody foresaw: its error, its hint and its type, each from the tool's name and what it threw.
        /// </summary>
        /// <remarks>
        /// As <c>ExplainFailures()</c>, for a server whose failures named the tool or pointed at its own log
        /// (Snail.MCP.Documents' "read_file failed: …"), and for one that throws exceptions of its own for a caller's
        /// mistake and words them as refusals (<see cref="FailureOptions.Refuse{TException}"/>). A server whose answers
        /// are text keeps its failures text too: <c>ExplainFailures(options =&gt; options.AnswersText = true)</c> answers
        /// them as <c>error — hint</c>, flagged <c>isError</c> (<see cref="FailureOptions.AnswersText"/>). Called again, the
        /// filter stays one and each <paramref name="configure"/> applies in turn.
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
                return AnswerTo(request, failure, words);
            }

            if (answer.IsError != true && ToolAnswer.IsFailure(answer))
                answer.IsError = true;

            return answer;
        };

    /// <summary>The answer to a call that threw: a refusal in its own words, arguments the call got wrong, or the server's failure.</summary>
    /// <remarks>
    /// One reading of an exception for a direct call and for a step of <c>run_tools</c>, so a step that throws is answered as
    /// the call would have been, in the envelope or, for a server whose answers are text, in words.
    /// </remarks>
    internal static CallToolResult AnswerTo(RequestContext<CallToolRequestParams> request, Exception failure, FailureOptions words)
    {
        var refused = FailureOf(request, failure, words);

        return words.AnswersText ? Words(refused) : ToolAnswer.Failed(refused.Error, refused.Hint, refused.Type, refused.Details);
    }

    /// <summary>What a call that threw is answered with, before it is written in either form.</summary>
    private static AnswerFailure FailureOf(RequestContext<CallToolRequestParams> request, Exception failure, FailureOptions words)
    {
        if (failure is ToolRefusalException refusal)
            return new AnswerFailure(refusal.Error, refusal.Hint, refusal.Type) { Details = refusal.Details };

        return ArgumentFaults.FaultOf(request, failure) ?? Unforeseen(request, failure, words);
    }

    /// <summary>A failure as plain text, the error and then the hint after a dash, flagged as an error: for a server whose answers are text.</summary>
    private static CallToolResult Words(AnswerFailure failure)
    {
        var text = failure.Hint is null ? failure.Error : $"{failure.Error} — {failure.Hint}";

        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = text }] };
    }

    /// <summary>A failure the tool did not answer itself, in the server's words, logged as the server's own when its type is <c>internal</c>.</summary>
    private static AnswerFailure Unforeseen(RequestContext<CallToolRequestParams> request, Exception failure, FailureOptions words)
    {
        var tool = request.Params?.Name ?? "a tool";
        var error = words.Error?.Invoke(tool, failure) ?? failure.Message;
        var named = words.RefusalOf(failure);
        var type = named?.Type ?? words.Type?.Invoke(tool, failure) ?? Internal;
        var isInternal = string.Equals(type, Internal, StringComparison.Ordinal);
        var loggers = request.Services?.GetService<ILoggerFactory>() ?? request.Server.Services?.GetService<ILoggerFactory>();

        if (loggers is not null)
        {
            var logger = loggers.CreateLogger(typeof(ToolFailures));

            if (isInternal)
                Failed(logger, failure, tool);
            else
                Refused(logger, tool, type, error);
        }

        return new AnswerFailure(error, HintOf(tool, failure, words, named, isInternal), type);
    }

    /// <summary>The hint of a failure the tool did not answer: the refusal's the server named, the server's own, or the default hint when the failure is the server's.</summary>
    private static string? HintOf(string tool, Exception failure, FailureOptions words, FailureOptions.Refusal? named, bool isInternal)
    {
        if (named is not null)
            return named.Hint?.Invoke(failure);

        if (ReferenceEquals(words.Hint, FailureOptions.InternalOnly))
            return isInternal ? Hint : null;

        return words.Hint?.Invoke(tool, failure);
    }

    private static bool IsUnforeseen(Exception failure, CancellationToken cancellationToken) =>
        failure is not McpProtocolException && !(failure is OperationCanceledException && cancellationToken.IsCancellationRequested);

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "{Tool} failed inside the server")]
    private static partial void Failed(ILogger logger, Exception failure, string tool);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "{Tool} refused the call ({Type}): {Error}")]
    private static partial void Refused(ILogger logger, string tool, string type, string error);

    /// <summary>The mark that the filter is registered, so a second call adds nothing.</summary>
    private sealed class Explained;
}

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
            return new AnswerFailure(refusal.Error) { Hint = refusal.Hint, Type = refusal.Type, Details = refusal.Details };

        return ArgumentFaults.FaultOf(request, failure) ?? Unforeseen(request, failure, words);
    }

    /// <summary>A failure as plain text, the error and then the hint after a dash, flagged as an error: for a server whose answers are text.</summary>
    private static CallToolResult Words(AnswerFailure failure)
    {
        var text = failure.Hint is null ? failure.Error : $"{failure.Error} — {failure.Hint}";

        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = text }] };
    }

    private static AnswerFailure Unforeseen(RequestContext<CallToolRequestParams> request, Exception failure, FailureOptions words) =>
        Unforeseen(request.Params?.Name ?? "a tool", failure, words, LoggersOf(request.Services ?? request.Server.Services), Hint);

    /// <summary>A failure a job's work or a program's command threw, in the words <c>ExplainFailures</c> gives a tool's: a refusal in its own, anything else as the server's failure.</summary>
    /// <remarks>
    /// One reading for a tool, a job and a command, so the words a server sets in <see cref="FailureOptions.Error"/> — to
    /// keep a path or a connection string out of what a model reads — hold for all three: a job's failure went out as the
    /// exception's message whatever the server had set, and a command that threw ended its program as a failure of
    /// <c>run_program</c> with the commands it had sent lost.
    /// </remarks>
    /// <param name="subject">What failed, as the words and the log name it: a job's kind, a program's command.</param>
    /// <param name="failure">What it threw.</param>
    /// <param name="services">The services the server's <see cref="FailureOptions"/> and loggers are read from; the defaults when null.</param>
    /// <param name="internalHint">The hint of a failure of the server the server words no hint for: a job's is null, since <c>JobAnswer</c> gives the hint of its options.</param>
    internal static AnswerFailure FailureOf(string subject, Exception failure, IServiceProvider? services, string? internalHint)
    {
        if (failure is ToolRefusalException refusal)
            return new AnswerFailure(refusal.Error) { Hint = refusal.Hint, Type = refusal.Type, Details = refusal.Details };

        FailureOptions words = services?.GetService<IOptions<FailureOptions>>()?.Value ?? new FailureOptions();

        return Unforeseen(subject, failure, words, LoggersOf(services), internalHint);
    }

    /// <summary>A failure nobody answered, in the server's words, logged as the server's own when its type is <c>internal</c>.</summary>
    private static AnswerFailure Unforeseen(string subject, Exception failure, FailureOptions words, ILoggerFactory? loggers, string? internalHint)
    {
        string error = words.Error?.Invoke(subject, failure) ?? failure.Message;
        FailureOptions.Refusal? named = words.RefusalOf(failure);
        string type = named?.Type ?? words.Type?.Invoke(subject, failure) ?? Internal;
        bool isInternal = string.Equals(type, Internal, StringComparison.Ordinal);

        if (loggers is not null)
        {
            ILogger logger = loggers.CreateLogger(typeof(ToolFailures));

            if (isInternal)
                Failed(logger, failure, subject);
            else
                Refused(logger, subject, type, error);
        }

        return new AnswerFailure(error) { Hint = HintOf(subject, failure, words, named, isInternal ? internalHint : null), Type = type };
    }

    private static ILoggerFactory? LoggersOf(IServiceProvider? services) => services?.GetService<ILoggerFactory>();

    /// <summary>The hint of a failure the tool did not answer: the refusal's the server named, the server's own, or the default hint when the failure is the server's.</summary>
    private static string? HintOf(string tool, Exception failure, FailureOptions words, FailureOptions.Refusal? named, string? internalHint)
    {
        if (named is not null)
            return named.Hint?.Invoke(failure);

        if (ReferenceEquals(words.Hint, FailureOptions.InternalOnly))
            return internalHint;

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

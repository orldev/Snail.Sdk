using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Testing;

namespace Snail.Sdk.Mcp.Tests;

/// <summary>A tool that throws, or answers a failure without flagging it, reaches the client as the failure envelope, flagged.</summary>
public sealed class ToolFailuresTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Tool_ThatThrows_IsAnsweredWithTheEnvelope_FlaggedAsAnError()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("throws");

        Assert.True(answer.IsError);
        Assert.False(answer.IsOk());
        Assert.Equal("The disk is full.", answer.Error());
        Assert.Equal(ToolFailures.Hint, answer.Hint());
        Assert.Equal("""{"ok":false,"error":"The disk is full.","hint":"The server failed inside, not because of the call; its log has the trace. Try once more, and if it fails the same way, report it instead of retrying.","type":"internal"}""", answer.Text());
    }

    [Fact]
    public async Task Tool_ThatThrows_IsLoggedOnce_WithItsTrace()
    {
        await using var stand = await Stand.StartAsync();

        await stand.Client.CallToolAsync("throws");

        var logged = Assert.Single(stand.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal(typeof(ToolFailures).FullName, logged.Category);
        Assert.Equal("throws failed inside the server", logged.Message);
        Assert.IsType<InvalidOperationException>(logged.Failure);
    }

    [Fact]
    public async Task Answer_ThatIsTheFailureEnvelope_ButNoError_IsFlagged()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("refuses");

        Assert.True(answer.IsError);
        Assert.Equal("""{"ok":false,"error":"No such file."}""", answer.Text());
    }

    [Fact]
    public async Task Answer_ThatIsASuccess_StaysOne()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("answers");

        Assert.NotEqual(true, answer.IsError);
        Assert.True(answer.IsOk());
    }

    /// <summary>A cancellation the client did not ask for — a bridge that timed out — is a failure like any other.</summary>
    [Fact]
    public async Task Cancellation_ThatTheClientDidNotAskFor_IsAnswered()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("times_out");

        Assert.True(answer.IsError);
        Assert.Equal("The bridge did not answer within 30 s.", answer.Error());
    }

    /// <summary>
    /// The call carries an id of the test's own and is cancelled by <c>notifications/cancelled</c> under it: the SDK's
    /// client numbers a request without writing the number back, so cancelling its token alone tells the server nothing.
    /// </summary>
    [Fact]
    public async Task Cancellation_ByTheClient_PassesThrough_AndIsNoFailure()
    {
        await using var stand = await Stand.StartAsync();
        using var cancelling = new CancellationTokenSource();
        var request = new JsonRpcRequest
        {
            Id = new RequestId($"call-{Guid.NewGuid():N}"),
            Method = RequestMethods.ToolsCall,
            Params = new JsonObject { ["name"] = "waits", ["arguments"] = new JsonObject() },
        };

        var call = stand.Client.SendRequestAsync(request, cancelling.Token);
        await stand.Waiting.Started.Task.WaitAsync(Patience);
        await stand.Client.SendNotificationAsync(NotificationMethods.CancelledNotification, new CancelledNotificationParams { RequestId = request.Id }, McpJsonUtilities.DefaultOptions, CancellationToken.None);
        await cancelling.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        await stand.Waiting.Cancelled.Task.WaitAsync(Patience);
        await Task.Delay(200);
        Assert.DoesNotContain(stand.Logs.Entries, entry => string.Equals(entry.Category, typeof(ToolFailures).FullName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProtocolError_ThatAToolThrows_StaysAProtocolError()
    {
        await using var stand = await Stand.StartAsync();

        await Assert.ThrowsAsync<McpProtocolException>(() => stand.Client.CallToolAsync("protocol").AsTask());
    }

    /// <summary>The filter goes first whenever it is registered, so it sees what a filter registered before it throws.</summary>
    [Fact]
    public async Task Filter_RegisteredBeforeIt_ThatThrows_IsExplainedToo()
    {
        await using var stand = await Stand.StartAsync(isGuarded: true);

        var answer = await stand.Client.CallToolAsync("answers");

        Assert.True(answer.IsError);
        Assert.Equal("The guard broke.", answer.Error());
    }

    /// <summary>A value the MCP SDK could not bind never reached the tool: "internal, try once more" had the model repeat the call as it was (Snail.MCP.Documents).</summary>
    [Fact]
    public async Task Call_WithAValueOfTheWrongKind_IsAnsweredAsTheArguments_NamingTheParameter()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("counts", new Dictionary<string, object?> { ["count"] = "many", ["label"] = "x" });

        Assert.True(answer.IsError);
        Assert.Equal("""'counts' cannot take "many" for 'count': it takes a whole number.""", answer.Error());
        Assert.Contains("\"type\":\"arguments\"", answer.Text(), StringComparison.Ordinal);
        Assert.Contains("'counts' takes count (required), label (required), stage.", answer.Hint(), StringComparison.Ordinal);
        Assert.DoesNotContain(stand.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Call_WithAWordTheEnumDoesNotHave_IsAnsweredAsTheArguments_WithTheWordsItHas()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("counts", new Dictionary<string, object?> { ["count"] = 1, ["label"] = "x", ["stage"] = "later" });

        Assert.True(answer.IsError);
        Assert.StartsWith("""'counts' cannot take "later" for 'stage': it takes one of""", answer.Error(), StringComparison.Ordinal);
    }

    /// <summary>A whole number past an int fits the schema, which bounds nothing, and still fails the binding: told by the trace, without a name.</summary>
    [Fact]
    public async Task Call_WithAValueTheSchemaAllowsButTheBindingRefuses_IsStillAnsweredAsTheArguments()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("counts", new Dictionary<string, object?> { ["count"] = 3_000_000_000L, ["label"] = "x" });

        Assert.True(answer.IsError);
        Assert.StartsWith("'counts' could not read the arguments of this call:", answer.Error(), StringComparison.Ordinal);
        Assert.Contains("\"type\":\"arguments\"", answer.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_ThatLeavesOutARequiredParameter_IsAnsweredAsTheArguments_NamingIt()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("counts", new Dictionary<string, object?> { ["count"] = 3 });

        Assert.True(answer.IsError);
        Assert.Equal("'counts' needs 'label', which this call did not send.", answer.Error());
        Assert.Contains("\"type\":\"arguments\"", answer.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Call_WithArgumentsThatFit_ReachesTheTool()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("counts", new Dictionary<string, object?> { ["count"] = 3, ["label"] = "x", ["stage"] = "inprogress" });

        Assert.NotEqual(true, answer.IsError);
        Assert.Equal("""{"ok":true,"data":"x:3"}""", answer.Text());
    }

    /// <summary>A tool's own code throws the same exceptions as the binding: with arguments that fit, it is the server's failure.</summary>
    [Fact]
    public async Task Tool_ThatThrowsAJsonExceptionOfItsOwn_WithArgumentsThatFit_IsAnInternalFailure()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("parses", new Dictionary<string, object?> { ["json"] = "{" });

        Assert.True(answer.IsError);
        Assert.Contains("\"type\":\"internal\"", answer.Text(), StringComparison.Ordinal);
        Assert.Equal(ToolFailures.Hint, answer.Hint());
    }

    [Fact]
    public async Task Failure_NobodyForesaw_IsWordedAsTheServerChose()
    {
        await using var stand = await Stand.StartAsync(configure: options =>
        {
            options.Error = (tool, failure) => $"{tool} failed: {failure.GetType().Name}";
            options.Hint = (_, _) => "Read the server's log.";
        });

        var answer = await stand.Client.CallToolAsync("throws");

        Assert.Equal("""{"ok":false,"error":"throws failed: InvalidOperationException","hint":"Read the server's log.","type":"internal"}""", answer.Text());
    }

    /// <summary>"Try once more" contradicted "call load_solution first" (Snail.MCP.Roslyn, Snail.MCP.CodeContext).</summary>
    [Fact]
    public async Task Refusal_ThatAToolThrows_IsAnsweredInItsOwnWords_AndNotLoggedAsTheServers()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("refuses_deep");

        Assert.True(answer.IsError);
        Assert.Equal("""{"ok":false,"error":"Solution 'a.sln' is not loaded.","hint":"Call load_solution first.","type":"notLoaded","data":{"loaded":[]}}""", answer.Text());
        Assert.DoesNotContain(stand.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Refusal_WithoutAHintOrAType_LeavesThemOut()
    {
        await using var stand = await Stand.StartAsync();

        var answer = await stand.Client.CallToolAsync("refuses_bare");

        Assert.Equal("""{"ok":false,"error":"No codebase to index."}""", answer.Text());
        Assert.Null(answer.Type());
    }

    /// <summary>A server that throws exceptions of its own for a caller's mistake tells them by their type.</summary>
    [Fact]
    public async Task Failure_OfAServersOwnKind_IsWordedAsTheRefusalItIs_AndNotLoggedAsTheServers()
    {
        await using var stand = await Stand.StartAsync(configure: options =>
        {
            options.Type = (_, failure) => failure is NotLoadedException ? "notLoaded" : null;
            options.Hint = (tool, failure) => failure is NotLoadedException ? $"call load_solution before {tool}" : ToolFailures.Hint;
        });

        var refused = await stand.Client.CallToolAsync("not_loaded");
        var failed = await stand.Client.CallToolAsync("throws");

        Assert.Equal("""{"ok":false,"error":"Solution 'a.sln' is not loaded.","hint":"call load_solution before not_loaded","type":"notLoaded"}""", refused.Text());
        Assert.Equal("internal", failed.Type());
        Assert.Equal(ToolFailures.Hint, failed.Hint());
        var logged = Assert.Single(stand.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal("throws failed inside the server", logged.Message);
    }

    /// <summary>Snail.MCP.Roslyn answers its failures without a hint.</summary>
    [Fact]
    public async Task Failure_WithTheHintTakenAway_HasNone()
    {
        await using var stand = await Stand.StartAsync(configure: options => options.Hint = null);

        var answer = await stand.Client.CallToolAsync("throws");

        Assert.Equal("""{"ok":false,"error":"The disk is full.","type":"internal"}""", answer.Text());
    }

    /// <summary>Snail.MCP.Obsidian set Type and Hint for its own exceptions and kept the default hint of the rest by hand.</summary>
    [Fact]
    public async Task Failure_OfAKindTheServerRefuses_IsWordedAsTheRefusal_AndTheRestKeepTheDefaultHint()
    {
        await using var stand = await Stand.StartAsync(configure: options => options.Refuse<NotLoadedException>("notLoaded", failure => $"load it first: {failure.Message}"));

        var refused = await stand.Client.CallToolAsync("not_loaded");
        var failed = await stand.Client.CallToolAsync("throws");

        Assert.True(refused.IsError);
        Assert.Equal("""{"ok":false,"error":"Solution 'a.sln' is not loaded.","hint":"load it first: Solution 'a.sln' is not loaded.","type":"notLoaded"}""", refused.Text());
        Assert.Equal("internal", failed.Type());
        Assert.Equal(ToolFailures.Hint, failed.Hint());
        var logged = Assert.Single(stand.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal("throws failed inside the server", logged.Message);
    }

    [Fact]
    public async Task Failure_OfAKindTheServerRefusesWithoutAHint_HasNone()
    {
        await using var stand = await Stand.StartAsync(configure: options => options.Refuse<NotLoadedException>("notLoaded"));

        var answer = await stand.Client.CallToolAsync("not_loaded");

        Assert.Equal("""{"ok":false,"error":"Solution 'a.sln' is not loaded.","type":"notLoaded"}""", answer.Text());
    }

    /// <summary>The first refusal an exception is answers it: a type derived from another is named first.</summary>
    [Fact]
    public async Task Refusals_AreTriedInTheOrderNamed_AndMatchDerivedTypes()
    {
        await using var stand = await Stand.StartAsync(configure: options => options
            .Refuse<NotLoadedException>("notLoaded")
            .Refuse<InvalidOperationException>("invalid", _ => "change the call"));

        var notLoaded = await stand.Client.CallToolAsync("not_loaded");
        var invalid = await stand.Client.CallToolAsync("throws");

        Assert.Equal("notLoaded", notLoaded.Type());
        Assert.Equal("invalid", invalid.Type());
        Assert.Equal("change the call", invalid.Hint());
    }

    /// <summary>"Try once more" is the server's own failure's hint: a failure named the caller's had the model repeat its mistake.</summary>
    [Fact]
    public async Task Failure_NamedByTypeAlone_GetsNoDefaultHint()
    {
        await using var stand = await Stand.StartAsync(configure: options => options.Type = (_, failure) => failure is NotLoadedException ? "notLoaded" : null);

        var refused = await stand.Client.CallToolAsync("not_loaded");
        var failed = await stand.Client.CallToolAsync("throws");

        Assert.Equal("""{"ok":false,"error":"Solution 'a.sln' is not loaded.","type":"notLoaded"}""", refused.Text());
        Assert.Equal(ToolFailures.Hint, failed.Hint());
    }

    /// <summary>Snail.MCP.Memory and Plans answer in words a command or a hook reads as they are: a failure in JSON among them was a second form.</summary>
    [Fact]
    public async Task Failure_OfAServerThatAnswersText_IsPlainText_ErrorThenHint_FlaggedAsAnError()
    {
        await using var stand = await Stand.StartAsync(configure: options => options.AnswersText = true);

        var answer = await stand.Client.CallToolAsync("throws");

        Assert.True(answer.IsError);
        Assert.Equal($"The disk is full. — {ToolFailures.Hint}", answer.Text());
        Assert.Null(answer.StructuredContent);
        var logged = Assert.Single(stand.Logs.Entries, entry => entry.Level >= LogLevel.Warning);
        Assert.Equal("throws failed inside the server", logged.Message);
    }

    [Fact]
    public async Task Refusal_OfAServerThatAnswersText_IsPlainText()
    {
        await using var stand = await Stand.StartAsync(configure: options => options.AnswersText = true);

        var refused = await stand.Client.CallToolAsync("refuses_deep");
        var bare = await stand.Client.CallToolAsync("refuses_bare");

        Assert.True(refused.IsError);
        Assert.Equal("Solution 'a.sln' is not loaded. — Call load_solution first.", refused.Text());
        Assert.True(bare.IsError);
        Assert.Equal("No codebase to index.", bare.Text());
    }

    [Fact]
    public async Task Arguments_ThatDoNotBind_OfAServerThatAnswersText_ArePlainText()
    {
        await using var stand = await Stand.StartAsync(configure: options => options.AnswersText = true);

        var answer = await stand.Client.CallToolAsync("counts", new Dictionary<string, object?> { ["label"] = "a" });

        Assert.True(answer.IsError);
        Assert.StartsWith("'counts' needs 'count', which this call did not send. — ", answer.Text(), StringComparison.Ordinal);
        Assert.DoesNotContain("{", answer.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failure_OfAKindTheServerRefuses_OfAServerThatAnswersText_IsPlainText()
    {
        await using var stand = await Stand.StartAsync(configure: options =>
        {
            options.AnswersText = true;
            options.Refuse<NotLoadedException>("notLoaded", _ => "call load_solution first");
        });

        var answer = await stand.Client.CallToolAsync("not_loaded");

        Assert.Equal("Solution 'a.sln' is not loaded. — call load_solution first", answer.Text());
    }

    [Fact]
    public void ExplainFailures_CalledTwice_RegistersOnce()
    {
        var services = new ServiceCollection();
        services.AddMcpServer().ExplainFailures().ExplainFailures();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Single(options.Filters.Request.CallToolFilters);
    }

    /// <summary>The sample server: a host with the failing tools over a pair of pipes, its log collected, and the client connected to it.</summary>
    private sealed class Stand : IAsyncDisposable
    {
        private Stand(IHost host, McpClient client)
        {
            Host = host;
            Client = client;
        }

        public IHost Host { get; }

        public McpClient Client { get; }

        public Collected Logs => Host.Services.GetRequiredService<Collected>();

        public Waiting Waiting => Host.Services.GetRequiredService<Waiting>();

        public static async Task<Stand> StartAsync(bool isGuarded = false, Action<FailureOptions>? configure = null)
        {
            var toServer = new Pipe();
            var toClient = new Pipe();
            var builder = McpHost.CreateBuilder([]);
            var logs = new Collected();

            builder.Logging.AddProvider(logs);
            builder.Services.AddSingleton(logs);
            builder.Services.AddSingleton<Waiting>();

            var server = builder.Services.AddMcpServer().WithStreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()).WithTools<FailingTools>();

            if (isGuarded)
                server.WithRequestFilters(filters => filters.AddCallToolFilter(Guard));

            if (configure is null)
                server.ExplainFailures();
            else
                server.ExplainFailures(configure);

            var host = builder.Build();
            await host.StartAsync();
            var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));

            return new Stand(host, client);
        }

        /// <summary>A filter that breaks on every call.</summary>
        private static McpRequestHandler<CallToolRequestParams, CallToolResult> Guard(McpRequestHandler<CallToolRequestParams, CallToolResult> _) =>
            (_, _) => throw new InvalidOperationException("The guard broke.");

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Host.StopAsync();
            Host.Dispose();
        }
    }

    /// <summary>Where the waiting tool says it started and that its call was cancelled.</summary>
    public sealed class Waiting
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Tools that fail every way a tool can.</summary>
    [McpServerToolType]
    public sealed class FailingTools(Waiting waiting)
    {
        [McpServerTool(Name = "throws"), Description("Throws.")]
        public static string Throws() => throw new InvalidOperationException("The disk is full.");

        [McpServerTool(Name = "refuses_deep"), Description("Throws a refusal from deep inside.")]
        public static string RefusesDeep() => throw new ToolRefusalException("Solution 'a.sln' is not loaded.", "Call load_solution first.", "notLoaded") { Details = new JsonObject { ["loaded"] = new JsonArray() } };

        [McpServerTool(Name = "refuses_bare"), Description("Throws a refusal with no hint.")]
        public static string RefusesBare() => throw new ToolRefusalException("No codebase to index.");

        [McpServerTool(Name = "not_loaded"), Description("Throws an exception of the server's own for a caller's mistake.")]
        public static string NotLoaded() => throw new NotLoadedException("Solution 'a.sln' is not loaded.");

        [McpServerTool(Name = "refuses"), Description("Answers the failure envelope as a string.")]
        public static string Refuses() => """{"ok":false,"error":"No such file."}""";

        [McpServerTool(Name = "answers"), Description("Answers the success envelope as a string.")]
        public static string Answers() => """{"ok":true,"data":1}""";

        [McpServerTool(Name = "times_out"), Description("Throws a cancellation nobody asked for.")]
        public static string TimesOut() => throw new TaskCanceledException("The bridge did not answer within 30 s.");

        [McpServerTool(Name = "protocol"), Description("Throws a protocol error.")]
        public static string Protocol() => throw new McpProtocolException("Bad argument.", McpErrorCode.InvalidParams);

        [McpServerTool(Name = "counts"), Description("Counts a label.")]
        public static string Counts([Description("How many.")] int count, [Description("What.")] string label, [Description("When.")] Stage stage = Stage.Queued) =>
            $$"""{"ok":true,"data":"{{label}}:{{count}}"}""";

        [McpServerTool(Name = "parses"), Description("Parses JSON of its own and throws on a bad one.")]
        public static string Parses([Description("The JSON.")] string json) => JsonNode.Parse(json)!.ToJsonString();

        [McpServerTool(Name = "waits"), Description("Waits until cancelled.")]
        public async Task<string> WaitsAsync(CancellationToken cancellationToken)
        {
            waiting.Started.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                waiting.Cancelled.TrySetResult();
                throw;
            }

            return "never";
        }
    }

    /// <summary>A caller's mistake a sample server throws an exception of its own for.</summary>
    public sealed class NotLoadedException(string message) : InvalidOperationException(message);

    /// <summary>The stages a sample parameter takes.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Stage>))]
    public enum Stage
    {
        Queued,
        InProgress,
    }

    /// <summary>One line of the server's log.</summary>
    public sealed record Entry(string Category, LogLevel Level, string Message, Exception? Failure);

    /// <summary>The server's log, kept for a test to read.</summary>
    public sealed class Collected : ILoggerProvider
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Collector(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Collector(Collected owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue(new Entry(category, logLevel, formatter(state, exception), exception));
        }
    }
}

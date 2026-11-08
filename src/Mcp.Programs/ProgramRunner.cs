using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;
using Acornima;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Interop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Answers;
using Snail.Sdk.Mcp.Hosting.Sessions;

namespace Snail.Sdk.Mcp.Programs;

/// <summary>Runs a JavaScript program whose only door to the outside is a set of commands: no files, no network, no clock but its own.</summary>
/// <remarks>
/// <para>
/// The engine, Jint, is made without access to the host's types, so <c>System</c>, the file system and the process are
/// out of a program's reach: everything it does, it does by calling a command, under the same refusals a tool meets.
/// A program is a cheaper way to spend a call, not a way out of the session.
/// </para>
/// <para>
/// It is bounded six ways (<see cref="ProgramOptions"/>): the time it was given, its memory, how deep its calls nest, how
/// many statements it runs, how many commands it sends, and, before every command, the session's gate. The engine is
/// synchronous, so the program runs on a thread of its own and each command waits there for its answer; a command still
/// running when the time is up is given up on, so a program never outlives its timeout by more than the command's
/// own reaction to cancellation. A timeout test therefore spends its time in slow commands: an empty
/// <c>while (true) {}</c> meets the statement bound in about 50 ms, long before any timeout.
/// </para>
/// </remarks>
public sealed class ProgramRunner(ProgramOptions options)
{
    /// <summary>Wraps the commands' object so that a name it does not hold answers with the names nearest to it.</summary>
    /// <remarks>
    /// A plain object answered every miss with "Property 'remove_layers' of object is not a function" — the miss and
    /// nothing about what to call instead (Snail.MCP.AfterEffects). A proxy judges the name where the program reads it,
    /// and the object still holds exactly the commands. Symbols, <c>then</c> and <c>toJSON</c> pass through: the engine
    /// asks for them itself when the object is awaited or written as JSON.
    /// </remarks>
    private const string Guard = """
        (function (commands, missing) {
            return new Proxy(commands, {
                get: function (catalog, name) {
                    if (typeof name !== 'string' || name in catalog || name === 'then' || name === 'toJSON') {
                        return catalog[name];
                    }
                    return missing(name);
                }
            });
        })
        """;

    /// <summary>How many names the answer to an unknown command offers as the nearest.</summary>
    private const int NearestCount = 3;

    /// <summary>How many commands a hint lists before it counts the rest.</summary>
    private const int ListedCommands = 40;

    private const string SentStand = "the commands it had already sent stand";

    /// <summary>Runs <paramref name="code"/>, the body of a function whose <c>return</c> is the result, over <paramref name="commands"/>.</summary>
    /// <param name="commands">What the global object of <see cref="ProgramOptions.Global"/> offers.</param>
    /// <param name="code">The program: JavaScript, run in strict mode.</param>
    /// <param name="input">What the program reads as <c>input</c>; an empty object when null.</param>
    /// <param name="timeout">How long it may run; <see cref="ProgramOptions.Timeout"/> when null.</param>
    /// <param name="refusal">Why the session may not call a tool, or null when it may, asked before every command with <see cref="IProgramCommands.ToolOf"/> — the command's own name where that is null; every command is allowed when null.</param>
    /// <param name="context">The request of <c>run_program</c>, handed to every command on <see cref="CommandCall.Request"/>, whose server's hint (<see cref="ToolForwarding.Hint"/>) a refused command carries; when null, the commands are called without it and a refusal carries <see cref="GrantOptions.Hint"/>.</param>
    /// <param name="cancellationToken">Ends the program, which then reports <see cref="ProgramFailures.Cancelled"/>.</param>
    public Task<ProgramRun> RunAsync(
        IProgramCommands commands,
        string code,
        JsonObject? input = null,
        TimeSpan? timeout = null,
        Func<string, string?>? refusal = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(code);

        var given = timeout ?? options.Timeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(given, TimeSpan.Zero, nameof(timeout));

        return Task.Run(() =>
        {
            using var execution = new Execution(options, commands, refusal, context, given, cancellationToken);

            return execution.Run(code, input);
        });
    }

    /// <summary>One program on its engine: what it logged and sent, and the time it has left.</summary>
    private sealed class Execution : IDisposable
    {
        private readonly ProgramOptions _options;
        private readonly IProgramCommands _commands;
        private readonly Func<string, string?>? _refusal;
        private readonly RequestContext<CallToolRequestParams>? _context;
        private readonly TimeSpan _timeout;
        private readonly CancellationToken _caller;
        private readonly CancellationTokenSource _deadline;
        private readonly DateTimeOffset _ends;
        private readonly Engine _engine;
        private readonly List<string> _log = [];
        private readonly List<ProgramCall> _calls = [];
        private readonly Stopwatch _running = Stopwatch.StartNew();
        private readonly JsValue _parse;
        private readonly JsValue _stringify;

        public Execution(ProgramOptions options, IProgramCommands commands, Func<string, string?>? refusal, RequestContext<CallToolRequestParams>? context, TimeSpan timeout, CancellationToken caller)
        {
            _options = options;
            _commands = commands;
            _refusal = refusal;
            _context = context;
            _timeout = timeout;
            _caller = caller;
            _deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
            _ends = DateTimeOffset.UtcNow + timeout;
            _deadline.CancelAfter(timeout);
            _engine = new Engine(engine => engine
                .Strict()
                .LimitMemory(options.MemoryBytes)
                .LimitRecursion(options.MaxRecursion)
                .MaxStatements(options.MaxStatements)
                .CancellationToken(_deadline.Token));
            _parse = _engine.Evaluate("JSON.parse");
            _stringify = _engine.Evaluate("JSON.stringify");
        }

        public ProgramRun Run(string code, JsonObject? input)
        {
            try
            {
                _engine.SetValue("input", Parsed(input ?? []));
                _engine.SetValue("log", Host("log", Write));
                _engine.SetValue(_options.Global, Guarded(Catalog()));

                var answer = _engine.Evaluate($"(() => {{\n{code}\n}})()");

                return Finished(Serialized(answer), null);
            }
            catch (ProgramStopped stopped)
            {
                return Finished(null, stopped.Reason);
            }
            catch (JavaScriptException failure)
            {
                return Finished(null, Thrown(failure));
            }
            catch (ParseErrorException failure)
            {
                return Finished(null, NotJavaScript(failure.LineNumber, failure.Message));
            }
            catch (Exception failure) when (failure is ExecutionCanceledException or OperationCanceledException or TimeoutException)
            {
                return Finished(null, OutOfTime());
            }
            catch (RecursionDepthOverflowException)
            {
                return Finished(null, TooLarge($"the program nested its calls deeper than {_options.MaxRecursion} levels", $"write the recursion as a loop, or bound it; {SentStand}"));
            }
            catch (StatementsCountOverflowException)
            {
                return Finished(null, TooLarge($"the program ran more than {_options.MaxStatements} statements", $"split the work into several programs; {SentStand}"));
            }
            catch (MemoryLimitExceededException)
            {
                return Finished(null, TooLarge($"the program used more than the {_options.MemoryBytes / 1024 / 1024} MB of memory a program may use", $"split the work into several programs; {SentStand}"));
            }
        }

        public void Dispose()
        {
            _engine.Dispose();
            _deadline.Dispose();
        }

        private ProgramRun Finished(JsonNode? result, ProgramFailure? failure) => new(result, [.. _log], [.. _calls], failure);

        /// <summary>The commands as an object of functions, <c>git.commit({ message: "…" })</c>, and nothing else.</summary>
        /// <remarks>
        /// <c>log</c> is the program's own global and is not put here: Snail.MCP.Git has a command called <c>log</c>, and
        /// with both on one object <c>git.log</c> wrote a line instead of reading the history — silently, the call never
        /// sent and the answer merely empty.
        /// </remarks>
        private JsObject Catalog()
        {
            var catalog = new JsObject(_engine);

            foreach (var command in _commands.Names)
                catalog.FastSetDataProperty(command, Host(command, arguments => Send(command, arguments)));

            return catalog;
        }

        private ObjectInstance Guarded(JsObject catalog)
        {
            var missing = Host("missing", asked => Missing(asked.Length == 0 ? string.Empty : asked[0].ToString()));

            return _engine.Evaluate(Guard).Call(JsValue.Undefined, [catalog, missing]).AsObject();
        }

        /// <summary>A function that, called, stops the program with the commands nearest to the name it asked for.</summary>
        private ClrFunction Missing(string name)
        {
            var nearest = NearestNames.Of(name, _commands.Names, NearestCount);
            var error = nearest.Count == 0 ? $"no command '{name}'" : $"no command '{name}'; the nearest: {string.Join(", ", nearest)}";

            return Host(name, _ => throw new ProgramStopped(new ProgramFailure(ProgramFailures.NoSuchCommand, error, Listed())));
        }

        /// <summary>A function of the host a program calls, handed its arguments in an array of its own.</summary>
        /// <remarks>
        /// <para>
        /// Jint hands a call whose arguments are all literals, or none, its cache of them: an <c>object[]</c> reinterpreted
        /// as <c>JsValue[]</c> (<c>ExpressionCache.ArgumentListEvaluation</c>, Jint 4.17). Indexing it works; reading it as
        /// <c>IEnumerable&lt;JsValue&gt;</c> — LINQ, <c>string.Join</c>, a spread — dispatches an interface the array does
        /// not have, which the JIT forgives and Native AOT answers by failing fast. <c>log(1)</c>, <c>log()</c> and
        /// <c>globalThis.log(1)</c> ended the whole native server, SIGABRT and nothing on stderr, while
        /// <c>log.call(null, 1)</c>, which passes a real array, and every test run by the JIT passed — in Snail.MCP.Git's
        /// native binary first, which ran this code before the package did.
        /// </para>
        /// <para>
        /// The copy is made here, by index, for every function the engine is given, so no host function ever holds the
        /// engine's array: none can trip on its type, and none can write into the cache of a literal. A span would not do,
        /// since a span over a <c>JsValue[]</c> that is not one throws.
        /// </para>
        /// </remarks>
        private ClrFunction Host(string name, Func<JsValue[], JsValue> body) =>
            new(_engine, name, (_, arguments) => body(Owned(arguments)));

        private static JsValue[] Owned(JsValue[] arguments)
        {
            if (arguments.Length == 0)
                return [];

            var owned = new JsValue[arguments.Length];

            for (var i = 0; i < owned.Length; i++)
                owned[i] = arguments[i];

            return owned;
        }

        private string Listed()
        {
            var names = _commands.Names;
            var shown = string.Join(", ", names.Take(ListedCommands));
            var rest = names.Count > ListedCommands ? $" and {names.Count - ListedCommands} more" : string.Empty;

            return $"{_options.Global} has the commands {shown}{rest}; call one of them by its name";
        }

        /// <summary>One command the program sent, run to its answer before the program goes on.</summary>
        private JsValue Send(string command, JsValue[] arguments)
        {
            if (_calls.Count >= _options.MaxCalls)
            {
                throw new ProgramStopped(new ProgramFailure(
                    ProgramFailures.TooManyCalls,
                    $"a program sends at most {_options.MaxCalls} commands; this one asked for more",
                    $"split the work into several programs; {SentStand}"));
            }

            if (_deadline.IsCancellationRequested)
                throw new ProgramStopped(OutOfTime());

            JsonObject given = Arguments(command, arguments);
            string tool = _commands.ToolOf(command) ?? command;

            if (_refusal?.Invoke(tool) is { } refused)
            {
                _calls.Add(new ProgramCall(command, false, 0));

                throw new JavaScriptException(Failure(new AnswerFailure(refused) { Hint = Hint(tool), Type = ProgramFailures.NotGranted }));
            }

            TimeSpan started = _running.Elapsed;
            Answer<JsonNode?> answer = Answered(command, given, started);

            _calls.Add(new ProgramCall(command, answer.IsSuccess, Milliseconds(started)));

            return answer.TryGet(out JsonNode? data)
                ? Parsed(data)
                : throw new JavaScriptException(Failure(new AnswerFailure(answer.Error) { Hint = answer.Hint, Type = answer.Type, Details = answer.Details }));
        }

        /// <summary>The command's answer, a command that threw answered as its failure, and the program stopped when its time ran out.</summary>
        /// <remarks>
        /// <para>
        /// The wait is the design, not a shortcut: Jint calls a host function synchronously and expects its value back, and
        /// the program runs on a pool thread of its own with no synchronisation context to deadlock on.
        /// </para>
        /// <para>
        /// A command that threw was let through to the engine, which handed it on: the program ended as a failure of
        /// <c>run_program</c> itself, and the commands it had already sent left the trace. It is a failed command like any
        /// other now, in the words <c>ExplainFailures</c> gives a tool that threw, which the program may catch.
        /// </para>
        /// </remarks>
        [SuppressMessage("Usage", "VSTHRD002", Justification = "Jint's host functions are synchronous; the program runs on a pool thread of its own, and only that thread waits.")]
        private Answer<JsonNode?> Answered(string command, JsonObject given, TimeSpan started)
        {
            try
            {
                Task<Answer<JsonNode?>> sent = _commands.CallAsync(new CommandCall(command, given, _context, _ends), _deadline.Token);

                return sent.WaitAsync(_deadline.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (_deadline.IsCancellationRequested)
            {
                _calls.Add(new ProgramCall(command, false, Milliseconds(started)));

                throw new ProgramStopped(OutOfTime());
            }
            catch (Exception failure)
            {
                AnswerFailure told = ToolFailures.FailureOf(command, failure, _context?.Services ?? _context?.Server.Services, ToolFailures.Hint);

                return told.As<JsonNode?>();
            }
        }

        /// <summary>What a command refused for want of a grant tells the program to do next: the server's hint, as its door and forwarders give it.</summary>
        /// <remarks>It said "this session was not granted it" whatever the server's hint and on a stateless server too, which has no session.</remarks>
        private string Hint(string tool) =>
            _context is not null && (_context.Services ?? _context.Server.Services) is { } services ? ToolForwarding.Hint(services, tool) : GrantOptions.Hint;

        private int Milliseconds(TimeSpan started) => (int)(_running.Elapsed - started).TotalMilliseconds;

        /// <summary>The one object a command takes, or the program stopped: a command given a string would run with its arguments missing.</summary>
        private JsonObject Arguments(string command, JsValue[] arguments)
        {
            if (arguments.Length == 0 || arguments[0].IsUndefined() || arguments[0].IsNull())
                return [];

            if (arguments[0].IsObject() && !arguments[0].IsArray() && Serialized(arguments[0]) is JsonObject given)
                return given;

            throw new ProgramStopped(new ProgramFailure(
                ProgramFailures.Invalid,
                $"{_options.Global}.{command} takes one object of arguments, and was given {arguments[0].Type.ToString().ToLowerInvariant()}",
                $"call it as {_options.Global}.{command}({{ name: value }})"));
        }

        /// <summary>A failed command as the exception a program catches: <c>error</c> and <c>message</c>, <c>hint</c>, <c>type</c>, <c>details</c>.</summary>
        private JsObject Failure(AnswerFailure answer)
        {
            var failure = new JsObject(_engine);

            failure.FastSetDataProperty("error", answer.Error);
            failure.FastSetDataProperty("message", answer.Error);
            failure.FastSetDataProperty("hint", answer.Hint is null ? JsValue.Null : answer.Hint);
            failure.FastSetDataProperty("type", answer.Type is null ? JsValue.Null : answer.Type);
            failure.FastSetDataProperty("details", Parsed(answer.Details));

            return failure;
        }

        /// <summary>What the program threw and did not catch, with the line of the program it stopped on.</summary>
        /// <remarks>A failed command keeps its own type, so the reply says <c>notGranted</c> or the server's <c>NotFound</c> rather than only that a program stopped.</remarks>
        private ProgramFailure Thrown(JavaScriptException failure)
        {
            var line = Line(failure.Location.Start.Line);

            if (failure.Error.IsObject() && Text(failure.Error.AsObject().Get("error")) is { } error)
            {
                var thrown = failure.Error.AsObject();

                return new ProgramFailure(Text(thrown.Get("type")) ?? ProgramFailures.Runtime, $"the program stopped at line {line}: {error}", Text(thrown.Get("hint")), Node(thrown.Get("details")));
            }

            if (failure.Error.IsObject() && string.Equals(Text(failure.Error.AsObject().Get("name")), "SyntaxError", StringComparison.Ordinal))
                return NotJavaScript(failure.Location.Start.Line, failure.Message);

            return new ProgramFailure(ProgramFailures.Runtime, $"the program stopped at line {line}: {failure.Message}", $"fix the program and run it again; {SentStand}");
        }

        private static ProgramFailure NotJavaScript(int engineLine, string message) =>
            new(ProgramFailures.Syntax, $"the program is not valid JavaScript at line {Line(engineLine)}: {message}", "fix that line and run the program again");

        /// <summary>The line of the program a line of the engine is: the program runs inside a function that opens a line above it.</summary>
        private static int Line(int engineLine) => Math.Max(1, engineLine - 1);

        private ProgramFailure OutOfTime()
        {
            if (_caller.IsCancellationRequested)
                return new ProgramFailure(ProgramFailures.Cancelled, "the call that ran the program was cancelled", SentStand);

            var seconds = string.Create(CultureInfo.InvariantCulture, $"{_timeout.TotalSeconds:0.###}");

            return new ProgramFailure(ProgramFailures.Timeout, $"the program ran longer than the {seconds} seconds it was given", $"{SentStand}; give it more time with timeoutSeconds, or split the work");
        }

        private static ProgramFailure TooLarge(string error, string hint) => new(ProgramFailures.TooLarge, error, hint);

        private JsValue Write(JsValue[] written)
        {
            if (_log.Count >= _options.LogLines)
                return JsValue.Undefined;

            var line = string.Join(' ', written.Select(Said));
            var length = _options.LogLineLength;

            _log.Add(line.Length <= length ? line : $"{line[..length]}…");

            return JsValue.Undefined;
        }

        /// <summary>A value as a log line says it: an object as its JSON, since <c>[object Object]</c> tells the reader nothing.</summary>
        private string Said(JsValue value)
        {
            if (!value.IsObject())
                return value.ToString();

            try
            {
                var text = _stringify.Call(JsValue.Undefined, [value]);

                return text.IsString() ? text.AsString() : value.ToString();
            }
            catch (JavaScriptException)
            {
                return value.ToString();
            }
        }

        private static string? Text(JsValue value) => value.IsUndefined() || value.IsNull() ? null : value.ToString();

        private JsonNode? Node(JsValue value) => value.IsUndefined() || value.IsNull() ? null : Serialized(value);

        /// <summary>JSON both ways rather than a converter of this library's own: the engine's parser knows every shape a program can make.</summary>
        private JsValue Parsed(JsonNode? value) =>
            value is null ? JsValue.Null : _parse.Call(JsValue.Undefined, [value.ToJsonString(ToolAnswer.Wire)]);

        private JsonNode? Serialized(JsValue value)
        {
            var text = _stringify.Call(JsValue.Undefined, [value]);

            return text.IsUndefined() || text.IsNull() ? null : JsonNode.Parse(text.AsString());
        }
    }

    /// <summary>A program stopped by a rule of the server rather than by the program itself; no program can catch it.</summary>
    private sealed class ProgramStopped(ProgramFailure reason) : Exception(reason.Error)
    {
        public ProgramFailure Reason { get; } = reason;
    }
}

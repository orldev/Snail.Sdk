using Microsoft.Extensions.Logging;

namespace Snail.Sdk.Mcp.Hosting;

/// <summary>Log lines written straight to stderr, one line each, at whatever level the host's filters let through.</summary>
/// <remarks>
/// stdout carries the JSON-RPC conversation, so a log line there breaks the protocol. The console logger of
/// Microsoft.Extensions.Logging writes to stderr just as well, but brings options bound from configuration,
/// formatters and a thread of its own to queue lines on — for a server that logs a handful of lines in a day.
/// The generic host's line of a start refused for the settings is left out: it carried the trace of a refusal the
/// program says in one line itself (<see cref="StartRefusal"/>).
/// </remarks>
internal sealed class StderrLogging : ILoggerProvider
{
    private static readonly Lock Writing = new();

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new Line(categoryName);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private sealed class Line(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || IsRefusedStart(exception))
                return;

            var text = $"{Level(logLevel)}: {category}[{eventId.Id}] {formatter(state, exception)}{(exception is null ? string.Empty : $"{Environment.NewLine}{exception}")}";

            lock (Writing)
                Console.Error.WriteLine(text);
        }

        /// <summary>Whether this is the generic host's line of a start refused for the settings, which the refusal says itself (<see cref="StartRefusal"/>).</summary>
        private bool IsRefusedStart(Exception? exception) =>
            string.Equals(category, StartRefusal.HostCategory, StringComparison.Ordinal) && StartRefusal.IsRefusal(exception);

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            _ => "crit"
        };
    }
}

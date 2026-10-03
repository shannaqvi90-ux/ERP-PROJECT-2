using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Erp.Testing;

/// <summary>
/// The errors the app under test logged (level Error and above), kept so a test that receives a
/// 5xx can say what went wrong on the server: a problem answer carries only a trace id, never
/// internals.
/// </summary>
public sealed partial class ServerErrorLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    public IReadOnlyCollection<string> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    /// <summary>The text with the server's log entry for its trace id appended, when there is one.</summary>
    public string Annotate(string text)
    {
        var traceId = TraceIdRegex().Match(text);
        if (!traceId.Success)
        {
            return text;
        }
        var entry = _entries.FirstOrDefault(e => e.Contains(traceId.Groups[1].Value, StringComparison.Ordinal));
        return entry is null ? text : $"{text}\n    server: {entry}";
    }

    [GeneratedRegex("\"traceId\":\"([^\"]+)\"")]
    private static partial Regex TraceIdRegex();

    private sealed class Logger(ServerErrorLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var text = $"{category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                var innermost = exception;
                while (innermost.InnerException is not null)
                {
                    innermost = innermost.InnerException;
                }
                text += $" — {exception.GetType().Name}: {exception.Message}" +
                        (innermost != exception ? $" (innermost {innermost.GetType().Name}: {innermost.Message})" : "") +
                        $" at {exception.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
            }
            if (log._entries.Count < 10_000)
            {
                log._entries.Enqueue(text);
            }
        }
    }
}

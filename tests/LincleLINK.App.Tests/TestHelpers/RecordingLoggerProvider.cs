using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace LincleLINK.App.Tests.TestHelpers;

/// <summary>
/// In-process <see cref="ILoggerProvider"/> capturing every event (level, message,
/// properties, exception and the active BeginScope chain) for assertions on the
/// diagnostic pipeline (issue #17 D6). Scopes flow through an instance AsyncLocal so
/// they survive awaits inside an operation.
/// </summary>
internal sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<RecordedLog> _logs = new();

    public IReadOnlyList<RecordedLog> Logs => _logs.ToArray();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _logs);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly AsyncLocal<Stack<RecordedScope>?> _scopes = new();
        private readonly string _category;
        private readonly ConcurrentQueue<RecordedLog> _sink;

        public RecordingLogger(string category, ConcurrentQueue<RecordedLog> sink)
        {
            _category = category;
            _sink = sink;
        }

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            var stack = _scopes.Value ??= new Stack<RecordedScope>();
            var scope = new RecordedScope(state.ToString() ?? string.Empty);
            stack.Push(scope);
            return new ScopePopper(stack, scope);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scope = _scopes.Value is { Count: > 0 } stack
                ? string.Join(" -> ", stack.Reverse().Select(s => s.Name))
                : string.Empty;

            var properties = state is IReadOnlyList<KeyValuePair<string, object>> kvps
                ? kvps.ToDictionary(k => k.Key, k => k.Value)
                : new Dictionary<string, object>();

            _sink.Enqueue(new RecordedLog(
                logLevel,
                _category,
                formatter(state, exception),
                exception,
                scope,
                properties));
        }

        private sealed class ScopePopper : IDisposable
        {
            private readonly Stack<RecordedScope> _stack;
            private readonly RecordedScope _scope;

            public ScopePopper(Stack<RecordedScope> stack, RecordedScope scope)
            {
                _stack = stack;
                _scope = scope;
            }

            public void Dispose()
            {
                if (_stack.TryPeek(out var current) && ReferenceEquals(current, _scope))
                {
                    _stack.Pop();
                }
            }
        }

        private sealed record RecordedScope(string Name);
    }
}

internal sealed record RecordedLog(
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception,
    string Scope,
    IReadOnlyDictionary<string, object> Properties);

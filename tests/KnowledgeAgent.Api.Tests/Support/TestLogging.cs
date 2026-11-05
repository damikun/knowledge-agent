using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace KnowledgeAgent.Api.Tests.Support;

public sealed class TestLogSink
{
    private const int Capacity = 3000;
    private readonly ConcurrentQueue<string> _lines = new();

    public void Add(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > Capacity && _lines.TryDequeue(out _))
        {
        }
    }

    public string Dump(int last = 150) => string.Join(Environment.NewLine, _lines.ToArray().TakeLast(last));
}

public sealed class TestLoggerProvider(TestLogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, sink);

    public void Dispose()
    {
    }

    private sealed class TestLogger(string category, TestLogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTime.UtcNow:HH:mm:ss.fff} [{Level(logLevel)}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            sink.Add(line);
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            try
            {
                TestContext.Current.TestOutputHelper?.WriteLine(line);
            }
            catch (Exception)
            {
                // The test that started the app may have finished already, and the sink still has the line.
            }
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            _ => "crit",
        };
    }
}

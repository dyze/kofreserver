namespace Kofre.Server.Logging;

/// <summary>One logged message, its exception included.</summary>
internal sealed record LogEntry(DateTimeOffset Time, LogLevel Level, string Category, string Message)
{
    public override string ToString() => $"{Time.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ} {ShortName(Level)} {Category}: {Message}";

    private static string ShortName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "CRT",
    };
}

/// <summary>Turns each message into a <see cref="LogEntry"/> for its provider. The levels are filtered by the logging configuration.</summary>
internal sealed class EntryLogger(string category, TimeProvider clock, Action<LogEntry> write) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        write(new LogEntry(clock.GetUtcNow(), logLevel, category, exception is null ? message : $"{message}{Environment.NewLine}{exception}"));
    }
}

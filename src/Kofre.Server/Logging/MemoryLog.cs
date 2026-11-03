using Microsoft.Extensions.Options;

namespace Kofre.Server.Logging;

/// <summary>The latest log entries, kept in memory (<see cref="LogOptions.MemoryEntries"/>): the daily summary reads them.</summary>
internal sealed class MemoryLog(IOptions<LogOptions> options, TimeProvider clock) : ILoggerProvider
{
    private readonly Lock _lock = new();
    private readonly Queue<LogEntry> _entries = new();

    public ILogger CreateLogger(string categoryName) => new EntryLogger(categoryName, clock, Add);

    /// <summary>The entries at <paramref name="minimum"/> or above, logged after <paramref name="time"/>, oldest first.</summary>
    public IReadOnlyList<LogEntry> Since(DateTimeOffset time, LogLevel minimum)
    {
        lock (_lock)
        {
            return _entries.Where(entry => entry.Time > time && entry.Level >= minimum).ToList();
        }
    }

    public void Dispose()
    {
    }

    private void Add(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > options.Value.MemoryEntries)
            {
                _entries.Dequeue();
            }
        }
    }
}

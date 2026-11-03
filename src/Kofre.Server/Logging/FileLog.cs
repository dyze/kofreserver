using Microsoft.Extensions.Options;

namespace Kofre.Server.Logging;

/// <summary>
/// Writes the log to <c>kofre.log</c>. Past <see cref="LogOptions.MaxFileBytes"/> it becomes
/// <c>kofre.1.log</c>, the older ones shift, and the oldest past <see cref="LogOptions.MaxFiles"/> is deleted.
/// </summary>
internal sealed class FileLog(IOptions<LogOptions> options, TimeProvider clock) : ILoggerProvider
{
    private readonly Lock _lock = new();

    public static string NameOf(int index) => index == 0 ? "kofre.log" : $"kofre.{index}.log";

    public ILogger CreateLogger(string categoryName) => new EntryLogger(categoryName, clock, Write);

    public void Dispose()
    {
    }

    private void Write(LogEntry entry)
    {
        var settings = options.Value;
        if (settings.Folder.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(settings.Folder);
                var current = Path.Combine(settings.Folder, NameOf(0));
                if (File.Exists(current) && new FileInfo(current).Length >= settings.MaxFileBytes)
                {
                    Rotate(settings);
                }

                File.AppendAllText(current, entry + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A log line that cannot be written is lost, never a request: the console still has it.
            }
        }
    }

    private static void Rotate(LogOptions settings)
    {
        var oldest = Math.Max(settings.MaxFiles, 1) - 1;
        File.Delete(Path.Combine(settings.Folder, NameOf(oldest)));
        for (var index = oldest - 1; index >= 0; index--)
        {
            var file = Path.Combine(settings.Folder, NameOf(index));
            if (File.Exists(file))
            {
                File.Move(file, Path.Combine(settings.Folder, NameOf(index + 1)));
            }
        }
    }
}

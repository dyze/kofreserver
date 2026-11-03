namespace Kofre.Server.Logging;

/// <summary>The <c>Kofre:Logs</c> section: log files kept on the server, besides the console.</summary>
public sealed class LogOptions
{
    public const string Section = "Kofre:Logs";

    /// <summary>Where <c>kofre.log</c> and its older files are written; empty: no file.</summary>
    public string Folder { get; set; } = "logs";

    public int MaxFiles { get; set; } = 5;

    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Latest entries kept in memory, for the daily summary.</summary>
    public int MemoryEntries { get; set; } = 1000;
}

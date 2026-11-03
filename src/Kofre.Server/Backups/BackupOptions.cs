namespace Kofre.Server.Backups;

/// <summary>The <c>Kofre:Backup</c> section: a daily archive of the data folder and the configuration files.</summary>
public sealed class BackupOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Where the archives are written; outside the data folder.</summary>
    public string Folder { get; set; } = "backups";

    /// <summary>The server's local time of the daily backup.</summary>
    public TimeSpan Time { get; set; } = new(3, 0, 0);

    /// <summary>Files archived with the data folder, such as the configuration and its secrets. A default here would be added to, not replaced, by the configured ones.</summary>
    public List<string> ConfigFiles { get; set; } = [];

    /// <summary>How long the backup waits for the running writes; past it, the backup is skipped.</summary>
    public TimeSpan WritesTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public int KeepDaily { get; set; } = 7;

    public int KeepWeekly { get; set; } = 4;

    public int KeepMonthly { get; set; } = 12;
}

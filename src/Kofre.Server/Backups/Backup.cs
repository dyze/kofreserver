using System.Diagnostics;
using Kofre.Server.Email;
using Kofre.Server.Logging;
using Kofre.Server.Maintenance;
using Kofre.Server.Scheduling;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Backups;

/// <summary>The last backup: its archive, or why it failed.</summary>
internal sealed record BackupResult(DateTimeOffset Time, string? Archive, string? Failure);

/// <summary>
/// The daily backup. Maintenance mode is on while the archive is written, so no file changes in
/// it; old archives are then deleted (<see cref="BackupRetention"/>). A failure is emailed to the admin.
/// </summary>
internal sealed class Backup(
    IOptions<KofreOptions> options,
    IOptions<LogOptions> logOptions,
    MaintenanceMode maintenance,
    AdminMail mail,
    TimeProvider clock,
    ILogger<Backup> log) : DailyService(clock)
{
    public BackupResult? Last { get; private set; }

    protected override bool Enabled => Settings.Enabled;

    protected override TimeSpan Time => Settings.Time;

    private BackupOptions Settings => options.Value.Backup;

    public override async Task RunAsync(CancellationToken cancellation)
    {
        var settings = Settings;
        var name = BackupArchive.NameOf(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Clock.GetUtcNow(), Clock.LocalTimeZone).DateTime));
        if (!await maintenance.TurnOnAsync(settings.WritesTimeout, Clock, cancellation))
        {
            await FailAsync($"writes still running after {settings.WritesTimeout}", cancellation);
            return;
        }

        log.LogInformation("Maintenance on for the backup {Archive}", name);
        var watch = Stopwatch.StartNew();
        int deleted;
        try
        {
            Directory.CreateDirectory(settings.Folder);
            await BackupArchive.WriteAsync(Path.Combine(settings.Folder, name), options.Value.DataFolder, ConfigFiles(settings), Excluded(), cancellation);
            deleted = BackupRetention.Prune(settings.Folder, settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await FailAsync(exception.Message, cancellation);
            return;
        }
        finally
        {
            maintenance.TurnOff();
            log.LogInformation("Maintenance off");
        }

        Last = new BackupResult(Clock.GetUtcNow(), name, null);
        log.LogInformation("Backup {Archive} made in {Seconds:0.0} s; {Deleted} old archives deleted", name, watch.Elapsed.TotalSeconds, deleted);
    }

    private IEnumerable<string> ConfigFiles(BackupOptions settings)
    {
        foreach (var file in settings.ConfigFiles)
        {
            if (File.Exists(file))
            {
                yield return file;
            }
            else
            {
                log.LogWarning("Backup: the configuration file {File} is missing", file);
            }
        }
    }

    /// <summary>What the data folder may hold but is not data: backups, logs, the running marker.</summary>
    private List<string> Excluded()
    {
        var excluded = new List<string> { Path.GetFullPath(Settings.Folder), Path.GetFullPath(Path.Combine(options.Value.DataFolder, ServiceLifecycle.MarkerFile)) };
        if (logOptions.Value.Folder.Length > 0)
        {
            excluded.Add(Path.GetFullPath(logOptions.Value.Folder));
        }

        return excluded;
    }

    private async Task FailAsync(string reason, CancellationToken cancellation)
    {
        Last = new BackupResult(Clock.GetUtcNow(), null, reason);
        log.LogError("Backup failed: {Reason}", reason);
        await mail.SendAsync("backup failed", $"The backup at {Clock.GetUtcNow():u} failed: {reason}", cancellation);
    }
}

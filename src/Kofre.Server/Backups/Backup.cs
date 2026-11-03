using System.Diagnostics;
using Kofre.Server.Email;
using Kofre.Server.Logging;
using Kofre.Server.Maintenance;
using Kofre.Server.Scheduling;
using Kofre.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Backups;

/// <summary>The last backup: its archive and size, or why it failed.</summary>
internal sealed record BackupResult(DateTimeOffset Time, string? Archive, string? Failure, long Size = 0);

/// <summary>
/// The daily backup. Maintenance mode is on while the archive is written, so no file changes in
/// it; old archives are then deleted (<see cref="BackupRetention"/>). The result is emailed to
/// the admin: a failure always, a success when <see cref="EmailOptions.BackupDone"/>.
/// </summary>
internal sealed class Backup(
    IOptions<KofreOptions> options,
    IOptions<LogOptions> logOptions,
    MaintenanceMode maintenance,
    AdminMail mail,
    DiskWatch disks,
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
        disks.Check();
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

        var size = new FileInfo(Path.Combine(settings.Folder, name)).Length;
        Last = new BackupResult(Clock.GetUtcNow(), name, null, size);
        log.LogInformation("Backup {Archive} made in {Seconds:0.0} s, {Size}; {Deleted} old archives deleted", name, watch.Elapsed.TotalSeconds, ByteSize.Format(size), deleted);
        if (options.Value.Email.BackupDone)
        {
            await mail.SendAsync("backup done", $"The backup {name} is made: {ByteSize.Format(size)}, in {watch.Elapsed.TotalSeconds:0.0} s.{Environment.NewLine}"
                + $"{deleted} old archives deleted; {Archives(settings.Folder)} in {settings.Folder}.", cancellation);
        }
    }

    /// <summary>"23 archives, 4.5 GB": the archives in the backups folder.</summary>
    public static string Archives(string folder)
    {
        var archives = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).Where(file => BackupArchive.DayOf(Path.GetFileName(file)) is not null).Select(file => new FileInfo(file).Length).ToList()
            : [];
        return $"{archives.Count} archives, {ByteSize.Format(archives.Sum())}";
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

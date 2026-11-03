using System.IO.Compression;
using Kofre.Server.Backups;
using Kofre.Server.Email;
using Kofre.Server.Logging;
using Kofre.Server.Maintenance;
using Kofre.Server.Storage;
using Microsoft.Extensions.Logging;
using static Kofre.Server.Tests.StoreApiTests;

namespace Kofre.Server.Tests;

public sealed class BackupTests : IDisposable
{
    private readonly KofreServer _server = new(new() { ["Email:Enabled"] = "true", ["Backup:WritesTimeout"] = "00:00:00.05" });

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task ABackup_ArchivesTheDataAndConfiguration_InMaintenance()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);
        (await ada.PutAsync($"/v1/stores/{id}/files/a/b", Bytes("x"))).EnsureSuccessStatusCode();
        File.WriteAllText(Path.Combine(_server.Root, "appsettings.json"), "{}");
        var backup = _server.Service<Backup>();

        await backup.RunAsync(CancellationToken.None);

        var name = BackupArchive.NameOf(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_server.Clock.Now, TimeZoneInfo.Local).DateTime));
        using var zip = ZipFile.OpenRead(Path.Combine(_server.BackupsFolder, name));
        var entries = zip.Entries.Select(entry => entry.FullName).ToList();
        Assert.Contains("data/accounts.json", entries);
        Assert.Contains("config/appsettings.json", entries);
        Assert.Contains(entries, entry => entry.StartsWith("data/stores/", StringComparison.Ordinal) && entry.EndsWith("/a/b", StringComparison.Ordinal));
        Assert.DoesNotContain("data/" + ServiceLifecycle.MarkerFile, entries);
        var size = new FileInfo(Path.Combine(_server.BackupsFolder, name)).Length;
        Assert.Equal(new BackupResult(_server.Clock.Now, name, null, size), backup.Last);
        Assert.False(_server.Service<MaintenanceMode>().IsOn);
        var done = (await _server.Mail.Next("backup done")).Body;
        Assert.Contains($"{name} is made: {ByteSize.Format(size)}", done, StringComparison.Ordinal);
        Assert.Contains($"1 archives, {ByteSize.Format(size)}", done, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABackupDone_IsNotEmailed_WhenTurnedOff()
    {
        using var server = new KofreServer(new() { ["Email:Enabled"] = "true", ["Email:BackupDone"] = "false" });

        await server.Service<Backup>().RunAsync(CancellationToken.None);

        Assert.NotNull(server.Service<Backup>().Last!.Archive);
        Assert.Equal(0, server.Mail.Count("backup"));
    }

    [Fact]
    public async Task TheDisks_AreCheckedBeforeABackup()
    {
        _ = _server.Services;
        _server.Disk.Usage = new(10, 100);

        await _server.Service<Backup>().RunAsync(CancellationToken.None);

        Assert.NotNull(await _server.Mail.Next("disk space low"));
    }

    [Fact]
    public async Task AMissingConfigurationFile_IsLoggedAndSkipped()
    {
        await _server.Service<Backup>().RunAsync(CancellationToken.None);

        Assert.Null(_server.Service<Backup>().Last!.Failure);
        Assert.Contains(Problems(), entry => entry.Message.Contains("is missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WritesStillRunning_SkipTheBackup_AndTellTheAdmin()
    {
        var maintenance = _server.Service<MaintenanceMode>();
        maintenance.TryStartWrite();

        await _server.Service<Backup>().RunAsync(CancellationToken.None);

        Assert.Contains("writes still running", (await _server.Mail.Next("backup failed")).Body, StringComparison.Ordinal);
        Assert.False(maintenance.IsOn);
        Assert.False(Directory.Exists(_server.BackupsFolder));
        maintenance.EndWrite();
    }

    [Fact]
    public async Task AFailedArchive_EndsMaintenance_AndTellsTheAdmin()
    {
        Directory.CreateDirectory(_server.Root);
        File.WriteAllText(_server.BackupsFolder, "a file where the folder should be");
        var backup = _server.Service<Backup>();

        await backup.RunAsync(CancellationToken.None);

        Assert.NotNull(backup.Last!.Failure);
        Assert.NotNull(await _server.Mail.Next("backup failed"));
        Assert.False(_server.Service<MaintenanceMode>().IsOn);
    }

    [Fact]
    public async Task AnEnabledBackup_WaitsForItsTime_AndStopsWithTheServer()
    {
        var server = new KofreServer(new() { ["Backup:Enabled"] = "true" });
        await server.SignedIn();

        Assert.False(Directory.Exists(server.BackupsFolder));
        server.Dispose();
    }

    private IReadOnlyList<LogEntry> Problems() => _server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Warning);
}

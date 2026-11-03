using System.IO.Compression;
using Kofre.Server.Backups;

namespace Kofre.Server.Tests;

public sealed class BackupArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kofre-backup-tests-" + Guid.NewGuid().ToString("N"));

    public BackupArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task TheExcludedFilesAndFolders_AreLeftOut()
    {
        var data = Path.Combine(_root, "data");
        Write(data, "a.txt");
        Write(data, "logs/kofre.log");
        Write(data, "logs-not/b.txt");
        Write(data, "server.running");
        var archive = Path.Combine(_root, "archive.zip");

        await BackupArchive.WriteAsync(archive, data, [], [Path.Combine(data, "logs"), Path.Combine(data, "server.running")], CancellationToken.None);

        using var zip = ZipFile.OpenRead(archive);
        Assert.Equal(["data/a.txt", "data/logs-not/b.txt"], zip.Entries.Select(entry => entry.FullName).Order());
    }

    [Fact]
    public async Task AFailedArchive_LeavesNoFile()
    {
        var archive = Path.Combine(_root, "archive.zip");
        var data = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            BackupArchive.WriteAsync(archive, data, [Path.Combine(_root, "missing.json")], [], CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(_root));
    }

    [Theory]
    [InlineData("kofre-2026-11-03.zip", true)]
    [InlineData("kofre-2026-13-03.zip", false)]
    [InlineData("kofre-2026-11-03.zip.tmp", false)]
    [InlineData("other-2026-11-03.zip", false)]
    public void OnlyArchives_HaveADay(string name, bool isArchive) => Assert.Equal(isArchive, BackupArchive.DayOf(name) is not null);

    [Fact]
    public void TheLatestOfEachDayWeekAndMonth_AreKept()
    {
        var kept = BackupRetention.Kept(Days(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)), new BackupOptions());

        DateOnly[] expected =
        [
            .. Days(new DateOnly(2026, 3, 25), new DateOnly(2026, 3, 31)), // 7 days
            new(2026, 3, 22), new(2026, 3, 15), // the weeks before; March 29 and 31 are already kept
            new(2026, 2, 28), new(2026, 1, 31), // the months before
        ];
        Assert.Equal(expected.Order(), kept.Order());
    }

    [Fact]
    public void Pruning_DeletesTheArchivesNotKept_Only()
    {
        foreach (var day in Days(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)))
        {
            File.WriteAllText(Path.Combine(_root, BackupArchive.NameOf(day)), "");
        }

        File.WriteAllText(Path.Combine(_root, "notes.txt"), "");

        Assert.Equal(90 - 11, BackupRetention.Prune(_root, new BackupOptions()));
        Assert.Equal(12, Directory.EnumerateFiles(_root).Count());
    }

    private static IEnumerable<DateOnly> Days(DateOnly first, DateOnly last)
    {
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    private static void Write(string folder, string path)
    {
        var file = Path.Combine(folder, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, path);
    }
}

using System.Globalization;

namespace Kofre.Server.Backups;

/// <summary>
/// Which archives are kept: the latest of each of the last <see cref="BackupOptions.KeepDaily"/> days,
/// <see cref="BackupOptions.KeepWeekly"/> weeks and <see cref="BackupOptions.KeepMonthly"/> months
/// that have one. The others are deleted.
/// </summary>
internal static class BackupRetention
{
    public static IReadOnlySet<DateOnly> Kept(IEnumerable<DateOnly> days, BackupOptions settings)
    {
        var latestFirst = days.Distinct().OrderDescending().ToList();
        return latestFirst.Take(settings.KeepDaily)
            .Concat(LatestOfEach(latestFirst, day => (ISOWeek.GetYear(day.ToDateTime(TimeOnly.MinValue)), ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue))), settings.KeepWeekly))
            .Concat(LatestOfEach(latestFirst, day => (day.Year, day.Month), settings.KeepMonthly))
            .ToHashSet();
    }

    /// <summary>Deletes the archives in <paramref name="folder"/> not kept; returns how many.</summary>
    public static int Prune(string folder, BackupOptions settings)
    {
        var archives = Directory.EnumerateFiles(folder)
            .Select(file => (File: file, Day: BackupArchive.DayOf(Path.GetFileName(file))))
            .Where(archive => archive.Day is not null)
            .ToList();
        var kept = Kept(archives.Select(archive => archive.Day!.Value), settings);
        var deleted = archives.Where(archive => !kept.Contains(archive.Day!.Value)).ToList();
        deleted.ForEach(archive => File.Delete(archive.File));
        return deleted.Count;
    }

    private static IEnumerable<DateOnly> LatestOfEach<TPeriod>(List<DateOnly> latestFirst, Func<DateOnly, TPeriod> period, int count) =>
        latestFirst.GroupBy(period).Take(count).Select(group => group.First());
}

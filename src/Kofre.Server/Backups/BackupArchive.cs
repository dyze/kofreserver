using System.Globalization;
using System.IO.Compression;

namespace Kofre.Server.Backups;

/// <summary>One backup: a zip of the data folder under <c>data/</c> and the configuration files under <c>config/</c>.</summary>
internal static class BackupArchive
{
    private const string Prefix = "kofre-";
    private const string Extension = ".zip";
    private const string DayFormat = "yyyy-MM-dd";

    public static string NameOf(DateOnly day) => Prefix + day.ToString(DayFormat, CultureInfo.InvariantCulture) + Extension;

    /// <summary>The day of an archive's file name; null for any other file.</summary>
    public static DateOnly? DayOf(string fileName) =>
        fileName.StartsWith(Prefix, StringComparison.Ordinal) && fileName.EndsWith(Extension, StringComparison.Ordinal)
        && DateOnly.TryParseExact(fileName[Prefix.Length..^Extension.Length], DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;

    /// <summary>
    /// Writes the archive under a temporary name, then renames it: a failure leaves no archive.
    /// The <paramref name="excluded"/> files and folders of the data folder are left out.
    /// </summary>
    public static async Task WriteAsync(string path, string dataFolder, IEnumerable<string> configFiles, IReadOnlyList<string> excluded, CancellationToken cancellation)
    {
        var temporary = path + ".tmp";
        try
        {
            await using (var output = File.Create(temporary))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                var dataRoot = Path.GetFullPath(dataFolder);
                foreach (var file in Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories).Where(file => !IsUnder(file, excluded)))
                {
                    await AddAsync(zip, file, "data/" + Path.GetRelativePath(dataRoot, file).Replace('\\', '/'), cancellation);
                }

                foreach (var file in configFiles)
                {
                    await AddAsync(zip, file, "config/" + Path.GetFileName(file), cancellation);
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static bool IsUnder(string file, IReadOnlyList<string> excluded) =>
        excluded.Any(item => file == item || file.StartsWith(item + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static async Task AddAsync(ZipArchive zip, string file, string name, CancellationToken cancellation)
    {
        // Fast: the clients' files are encrypted, so they hardly compress.
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var target = entry.Open();
        await source.CopyToAsync(target, cancellation);
    }
}

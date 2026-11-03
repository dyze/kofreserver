namespace Kofre.Server.Storage;

/// <summary>The space of the disk a folder is on.</summary>
internal sealed record DiskUsage(long Free, long Total)
{
    public int FreePercent => Total > 0 ? (int)(Free * 100 / Total) : 100;

    public override string ToString() => $"{ByteSize.Format(Free)} free of {ByteSize.Format(Total)} ({FreePercent} %)";
}

/// <summary>Measures the disk a folder is on.</summary>
internal interface IDiskSpace
{
    DiskUsage Of(string folder);
}

/// <summary>Through the operating system: the folder, or its nearest parent that exists.</summary>
internal sealed class DriveSpace : IDiskSpace
{
    public DiskUsage Of(string folder)
    {
        var existing = Path.GetFullPath(folder);
        while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
        {
            existing = parent;
        }

        var drive = new DriveInfo(existing);
        return new DiskUsage(drive.AvailableFreeSpace, drive.TotalSize);
    }
}

internal static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>"12.3 MB": in units of 1024, one decimal past bytes.</summary>
    public static string Format(long bytes)
    {
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{size:0.0} {Units[unit]}");
    }

    /// <summary>The size of the files in a folder and below; 0 when there is none.</summary>
    public static long OfFolder(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) : 0;
}

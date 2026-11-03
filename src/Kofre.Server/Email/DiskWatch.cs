using Kofre.Server.Scheduling;
using Kofre.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Email;

/// <summary>
/// Checks the free space of the data and backups disks every hour and before each backup. The admin is
/// emailed when one falls below <see cref="EmailOptions.MinFreeDiskPercent"/>, and once all are back above it.
/// </summary>
internal sealed class DiskWatch(IOptions<KofreOptions> options, IDiskSpace disks, AdminMail mail, TimeProvider clock, ILogger<DiskWatch> log)
    : HourlyService(clock, log)
{
    private readonly Lock _lock = new();
    private HashSet<string> _low = [];

    /// <summary>The folders watched, by name, with their disk's space.</summary>
    public IReadOnlyList<(string Name, DiskUsage Usage)> Measure() =>
        [("data", disks.Of(options.Value.DataFolder)), ("backups", disks.Of(options.Value.Backup.Folder))];

    public void Check()
    {
        var minimum = options.Value.Email.MinFreeDiskPercent;
        IReadOnlyList<(string Name, DiskUsage Usage)> usages;
        HashSet<string> low;
        bool newlyLow, recovered;
        // Measured and compared at once: a check measuring earlier must not overwrite a later one (the hourly one and the backup's).
        lock (_lock)
        {
            try
            {
                usages = Measure();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.LogWarning(exception, "Disk space could not be measured");
                return;
            }

            low = usages.Where(item => item.Usage.FreePercent < minimum).Select(item => item.Name).ToHashSet();
            newlyLow = !low.IsSubsetOf(_low);
            recovered = low.Count == 0 && _low.Count > 0;
            _low = low;
        }

        var described = string.Join(Environment.NewLine, usages.Select(item => $"  {item.Name}: {item.Usage}"));
        if (newlyLow)
        {
            log.LogWarning("Disk space below {Minimum} % for {Folders}", minimum, string.Join(", ", low));
            mail.Post("disk space low", $"Free disk space is below {minimum} % (MinFreeDiskPercent) for {string.Join(", ", low)}:{Environment.NewLine}{described}");
        }
        else if (recovered)
        {
            log.LogInformation("Disk space back above {Minimum} %", minimum);
            mail.Post("disk space back", $"Free disk space is back above {minimum} %:{Environment.NewLine}{described}");
        }
    }

    protected override void Run() => Check();
}

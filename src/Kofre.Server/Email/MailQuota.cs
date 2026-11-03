using Kofre.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Email;

/// <summary>The emails sent in the last hour, and those not sent since the last one was.</summary>
internal sealed record MailLog(List<DateTimeOffset> Sent, int Suppressed);

/// <summary>
/// At most <see cref="EmailOptions.MaxPerHour"/> emails an hour. Kept in a file, so a server
/// restarting in a loop, each start emailed, cannot reset the count.
/// </summary>
internal sealed class MailQuota(IOptions<KofreOptions> options, TimeProvider clock)
{
    public const string FileName = "admin-mail.json";

    private readonly Lock _lock = new();

    private string FilePath => Path.Combine(options.Value.DataFolder, FileName);

    /// <summary>Takes one email from the quota: the emails suppressed before it, or null when it is suppressed too.</summary>
    public int? TryTake()
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            var log = JsonFile.Read(FilePath, new MailLog([], 0));
            var sent = log.Sent.Where(time => time > now - TimeSpan.FromHours(1)).ToList();
            if (sent.Count >= options.Value.Email.MaxPerHour)
            {
                JsonFile.Write(FilePath, new MailLog(sent, log.Suppressed + 1));
                return null;
            }

            JsonFile.Write(FilePath, new MailLog([.. sent, now], 0));
            return log.Suppressed;
        }
    }
}

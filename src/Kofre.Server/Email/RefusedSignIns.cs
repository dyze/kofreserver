using Microsoft.Extensions.Options;

namespace Kofre.Server.Email;

/// <summary>
/// Counts the sign-ins refused for a wrong password in the last hour. Reaching
/// <see cref="EmailOptions.MaxRefusedSignInsPerHour"/>, someone may be trying passwords: the admin is
/// told once, and again only after the count fell back below it.
/// </summary>
internal sealed class RefusedSignIns(IOptions<KofreOptions> options, AdminMail mail, TimeProvider clock, ILogger<RefusedSignIns> log)
{
    private readonly Lock _lock = new();
    private readonly Queue<DateTimeOffset> _refused = new();
    private bool _told;

    public void Add()
    {
        var now = clock.GetUtcNow();
        var limit = options.Value.Email.MaxRefusedSignInsPerHour;
        lock (_lock)
        {
            _refused.Enqueue(now);
            while (_refused.Peek() <= now - TimeSpan.FromHours(1))
            {
                _refused.Dequeue();
            }

            if (_refused.Count < limit)
            {
                _told = false;
                return;
            }

            if (_told)
            {
                return;
            }

            _told = true;
        }

        log.LogWarning("{Count} sign-ins refused in the last hour (MaxRefusedSignInsPerHour)", limit);
        mail.Post("many sign-ins refused", $"{limit} sign-ins were refused in the last hour for a wrong email or password: someone may be trying passwords. "
            + "Each address is limited per minute (SignInsPerMinute); the log has the details.");
    }
}

using System.Text;
using Kofre.Server.Accounts;
using Kofre.Server.Api;
using Kofre.Server.Backups;
using Kofre.Server.Logging;
using Kofre.Server.Scheduling;
using Kofre.Server.Stores;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Email;

/// <summary>Emails the admin a summary every day: accounts, stores, the last backup, and the warnings and errors logged since the last summary.</summary>
internal sealed class DailySummary(
    IOptions<KofreOptions> options,
    AdminMail mail,
    AccountStore accounts,
    StoreRepository stores,
    Backup backup,
    MemoryLog memory,
    TimeProvider clock) : DailyService(clock)
{
    public const int MaxLogLines = 50;

    private DateTimeOffset _since = clock.GetUtcNow();

    protected override bool Enabled => mail.Enabled;

    protected override TimeSpan Time => options.Value.Email.SummaryTime;

    public override async Task RunAsync(CancellationToken cancellation)
    {
        var now = Clock.GetUtcNow();
        var body = Write(now);
        _since = now;
        await mail.SendAsync("daily summary", body, cancellation);
    }

    private string Write(DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine($"Kofre server {VersionEndpoint.Current} on {Environment.MachineName}, {now:u} (since {_since:u}).").AppendLine();

        var all = accounts.All();
        var pending = all.Where(account => account.IsPending).ToList();
        text.AppendLine($"Accounts: {all.Count(account => account.IsActive)} active, {pending.Count} waiting for approval.");
        pending.ForEach(account => text.AppendLine($"  waiting: {account.Email} ({account.Id}), since {account.Created:u}"));
        text.AppendLine($"Stores: {stores.Count()}.");
        text.AppendLine(backup.Last switch
        {
            null => "Last backup: none since the server started.",
            { Failure: { } failure } => $"Last backup: FAILED at {backup.Last.Time:u}: {failure}",
            _ => $"Last backup: {backup.Last.Archive} at {backup.Last.Time:u}.",
        }).AppendLine();

        var problems = memory.Since(_since, LogLevel.Warning);
        text.AppendLine($"Warnings and errors: {problems.Count}.");
        foreach (var entry in problems.TakeLast(MaxLogLines))
        {
            text.AppendLine($"  {entry}");
        }

        return text.ToString();
    }
}

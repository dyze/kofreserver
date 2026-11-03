namespace Kofre.Server.Scheduling;

/// <summary>Runs a task every day at a local time of the server's clock.</summary>
internal abstract class DailyService(TimeProvider clock) : BackgroundService
{
    protected TimeProvider Clock => clock;

    protected abstract bool Enabled { get; }

    protected abstract TimeSpan Time { get; }

    public abstract Task RunAsync(CancellationToken cancellation);

    /// <summary>The wait from <paramref name="now"/> to the next <paramref name="time"/> of the day, in <paramref name="zone"/>.</summary>
    public static TimeSpan Until(DateTimeOffset now, TimeSpan time, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var next = local.Date + time;
        if (next <= local.DateTime)
        {
            next = next.AddDays(1);
        }

        return new DateTimeOffset(next, zone.GetUtcOffset(next)) - now;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (Enabled)
            {
                await Task.Delay(Until(clock.GetUtcNow(), Time, clock.LocalTimeZone), clock, stoppingToken);
                await RunAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The server stops.
        }
    }
}

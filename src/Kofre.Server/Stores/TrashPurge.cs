namespace Kofre.Server.Stores;

/// <summary>Purges the trash at start, then every hour.</summary>
internal sealed class TrashPurge(StoreRepository stores, TimeProvider clock, ILogger<TrashPurge> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                stores.PurgeTrash();
            }
            catch (IOException exception)
            {
                log.LogWarning(exception, "Purging the trash failed");
            }
        }
        while (await NextTick(timer, stoppingToken));
    }

    private static async Task<bool> NextTick(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

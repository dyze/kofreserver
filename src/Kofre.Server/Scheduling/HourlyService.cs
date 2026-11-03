namespace Kofre.Server.Scheduling;

/// <summary>Runs a task at start, then every hour. A failed run is logged; the next one comes all the same.</summary>
internal abstract class HourlyService(TimeProvider clock, ILogger log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected abstract void Run();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                Run();
            }
            catch (IOException exception)
            {
                log.LogWarning(exception, "{Task} failed", GetType().Name);
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

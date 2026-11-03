using Kofre.Server.Scheduling;

namespace Kofre.Server.Stores;

/// <summary>Purges the trash at start, then every hour.</summary>
internal sealed class TrashPurge(StoreRepository stores, TimeProvider clock, ILogger<TrashPurge> log) : HourlyService(clock, log)
{
    protected override void Run() => stores.PurgeTrash();
}

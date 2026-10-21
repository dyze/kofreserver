using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Stores;

/// <summary>The stores, one folder each under <c>stores/</c>, named by the store's id.</summary>
internal sealed class StoreRepository(IOptions<KofreOptions> options, TimeProvider clock, ILogger<StoreRepository> log)
{
    public const string FolderName = "stores";

    private readonly ConcurrentDictionary<Guid, Store> _open = new();

    private string Root => Path.Combine(options.Value.DataFolder, FolderName);

    public Store Create(Guid owner)
    {
        var id = Guid.NewGuid();
        var store = Store.Create(id, FolderOf(id), owner, clock.GetUtcNow());
        _open[id] = store;
        return store;
    }

    /// <summary>The store with this id, in the trash or not; null when there is none.</summary>
    public Store? Find(Guid id) =>
        _open.TryGetValue(id, out var store) ? store
        : File.Exists(Path.Combine(FolderOf(id), Store.InfoFile)) ? _open.GetOrAdd(id, _ => new Store(id, FolderOf(id)))
        : null;

    /// <summary>The stores <paramref name="account"/> is a member of, not in the trash.</summary>
    public IReadOnlyList<StoreSummary> ListFor(Guid account) =>
        All()
            .Select(store => (Store: store, Info: store.Info))
            .Where(item => item.Info.Deleted is null && item.Info.Members.ContainsKey(account))
            .Select(item =>
            {
                var stats = item.Store.Stats();
                return new StoreSummary(item.Store.Id, item.Info.Members[account], stats.Files, stats.Bytes, item.Info.Fence);
            })
            .ToList();

    /// <summary>Deletes for good the stores in the trash for longer than <see cref="KofreOptions.TrashDays"/>.</summary>
    public int PurgeTrash()
    {
        var limit = clock.GetUtcNow() - TimeSpan.FromDays(options.Value.TrashDays);
        var purged = 0;
        foreach (var store in All().Where(store => store.Info.Deleted <= limit))
        {
            _open.TryRemove(store.Id, out _);
            Directory.Delete(store.Folder, recursive: true);
            log.LogInformation("Store {Store} purged from the trash", store.Id);
            purged++;
        }

        return purged;
    }

    private IEnumerable<Store> All() =>
        Directory.Exists(Root)
            ? Directory.EnumerateDirectories(Root)
                .Select(folder => Guid.TryParse(Path.GetFileName(folder), out var id) ? Find(id) : null)
                .OfType<Store>()
                .ToList()
            : [];

    private string FolderOf(Guid id) => Path.Combine(Root, id.ToString("N"));
}

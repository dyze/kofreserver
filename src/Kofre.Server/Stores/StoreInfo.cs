namespace Kofre.Server.Stores;

/// <summary>What a member may do in a store; each role includes the ones before it.</summary>
public enum Role
{
    Reader,
    Writer,
    Owner,
}

/// <summary>
/// A store's <c>store.json</c>: its members, when it was put in the trash (null: it is not), and
/// its fence (null: off): once on, an upload must name a number at least as high.
/// </summary>
public sealed record StoreInfo(Dictionary<Guid, Role> Members, DateTimeOffset Created, DateTimeOffset? Deleted = null, long? Fence = null);

/// <summary>A line of a store's feed: the <paramref name="Seq"/>-th file added.</summary>
public sealed record FeedEntry(long Seq, string Path, long Size);

/// <summary>A page of a store's feed, and whether more entries follow.</summary>
public sealed record FeedPage(IReadOnlyList<FeedEntry> Entries, bool More);

/// <summary>How many files a store holds, and their size in bytes.</summary>
public sealed record StoreStats(int Files, long Bytes);

/// <summary>A store as listed for one of its members.</summary>
public sealed record StoreSummary(Guid Id, Role Role, int Files, long Bytes, long? Fence = null);

/// <summary>A member of a store, and its role.</summary>
public sealed record Member(Guid Account, Role Role);

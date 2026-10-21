using System.Text;
using System.Text.Json;
using Kofre.Server.Storage;

namespace Kofre.Server.Stores;

/// <summary>What became of an upload.</summary>
public enum AddResult
{
    Added,

    /// <summary>A file is at this path already: files are never replaced.</summary>
    Exists,

    TooLarge,

    /// <summary>The store's fence is higher than the upload's: it was raised meanwhile.</summary>
    Fenced,
}

/// <summary>
/// One store on disk: <c>store.json</c> (members, trash), <c>feed.jsonl</c> (one line per file
/// added, in order) and <c>files/</c>. Files are only added, each under a new path. Thread-safe.
/// </summary>
internal sealed class Store
{
    public const string InfoFile = "store.json";
    public const string FeedFile = "feed.jsonl";
    public const string FilesFolder = "files";
    public const string IncomingFolder = "incoming";

    private readonly Lock _lock = new();
    private List<FeedEntry>? _feed;
    private Dictionary<string, FeedEntry> _byPath = new(StringComparer.Ordinal);

    public Store(Guid id, string folder)
    {
        Id = id;
        Folder = folder;
    }

    public Guid Id { get; }

    public string Folder { get; }

    /// <summary>Its <c>store.json</c>, read again at every call so the admin's edits apply at once.</summary>
    public StoreInfo Info
    {
        get
        {
            lock (_lock)
            {
                return JsonFile.Read<StoreInfo?>(InfoPath, null) ?? throw new InvalidDataException($"Store {Id} has no {InfoFile}");
            }
        }
    }

    private string InfoPath => Path.Combine(Folder, InfoFile);

    private string FeedPath => Path.Combine(Folder, FeedFile);

    public static Store Create(Guid id, string folder, Guid owner, DateTimeOffset now)
    {
        Directory.CreateDirectory(Path.Combine(folder, FilesFolder));
        JsonFile.Write(Path.Combine(folder, InfoFile), new StoreInfo(new() { [owner] = Role.Owner }, now));
        return new Store(id, folder);
    }

    public Role? RoleOf(Guid account) => Info.Members.TryGetValue(account, out var role) ? role : null;

    public void SetDeleted(DateTimeOffset? when) => Change(info => info with { Deleted = when });

    /// <summary>Gives <paramref name="account"/> a role, or removes it (null).</summary>
    public void SetMember(Guid account, Role? role) => Change(info =>
    {
        var members = new Dictionary<Guid, Role>(info.Members);
        if (role is { } given)
        {
            members[account] = given;
        }
        else
        {
            members.Remove(account);
        }

        return info with { Members = members };
    });

    /// <summary>Turns the fence on or raises it to <paramref name="fence"/>, or turns it off (null). False when it would lower it.</summary>
    public bool SetFence(long? fence)
    {
        lock (_lock)
        {
            if (fence < Info.Fence)
            {
                return false;
            }

            Change(info => info with { Fence = fence });
            return true;
        }
    }

    /// <summary>Whether an upload naming <paramref name="fence"/> (null: none) passes the store's fence.</summary>
    public bool Admits(long? fence) => Info.Fence is not { } current || fence >= current;

    private void Change(Func<StoreInfo, StoreInfo> change)
    {
        lock (_lock)
        {
            JsonFile.Write(InfoPath, change(Info));
        }
    }

    public FeedPage FeedSince(long since, int max)
    {
        lock (_lock)
        {
            var after = Feed().Where(entry => entry.Seq > since).ToList();
            return new FeedPage(after.Take(max).ToList(), after.Count > max);
        }
    }

    public StoreStats Stats()
    {
        lock (_lock)
        {
            return new StoreStats(Feed().Count, Feed().Sum(entry => entry.Size));
        }
    }

    /// <summary>The file at <paramref name="path"/> on disk; null when the store has none.</summary>
    public string? FileOf(string path)
    {
        lock (_lock)
        {
            Feed();
            return _byPath.ContainsKey(path) ? FullPathOf(path) : null;
        }
    }

    /// <summary>
    /// Adds a file: streamed to <c>incoming/</c> first, then moved to its path and added to the
    /// feed. A file found at its path but missing from the feed (a crash between both) is added to
    /// the feed then, as the move had completed. The fence is checked again as the file is added.
    /// </summary>
    public async Task<AddResult> AddAsync(string path, Stream content, long maxBytes, long? fence, CancellationToken cancel)
    {
        if (FileOf(path) is not null)
        {
            return AddResult.Exists;
        }

        var incoming = Path.Combine(Folder, IncomingFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(incoming)!);
        try
        {
            if (!await CopyAtMost(content, incoming, maxBytes, cancel))
            {
                return AddResult.TooLarge;
            }

            return Commit(path, incoming, fence);
        }
        finally
        {
            File.Delete(incoming);
        }
    }

    private AddResult Commit(string path, string incoming, long? fence)
    {
        lock (_lock)
        {
            if (!Admits(fence))
            {
                return AddResult.Fenced;
            }

            Feed();
            if (_byPath.ContainsKey(path))
            {
                return AddResult.Exists;
            }

            var target = FullPathOf(path);
            if (File.Exists(target))
            {
                Append(path, new FileInfo(target).Length);
                return AddResult.Exists;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(incoming, target);
            Append(path, new FileInfo(target).Length);
            return AddResult.Added;
        }
    }

    private static async Task<bool> CopyAtMost(Stream content, string path, long maxBytes, CancellationToken cancel)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancel)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                return false;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancel);
        }

        await output.FlushAsync(cancel);
        return true;
    }

    private string FullPathOf(string path) => Path.Combine([Folder, FilesFolder, .. path.Split('/')]);

    private void Append(string path, long size)
    {
        var entry = new FeedEntry(_feed!.Count == 0 ? 1 : _feed[^1].Seq + 1, path, size);
        using (var output = new FileStream(FeedPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            output.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JsonFile.Options).ReplaceLineEndings("") + "\n"));
            output.Flush(flushToDisk: true);
        }

        _feed.Add(entry);
        _byPath[path] = entry;
    }

    /// <summary>The feed, read once; a line cut by a crash is dropped (its file is re-added by <see cref="Commit"/>).</summary>
    private List<FeedEntry> Feed()
    {
        if (_feed is not null)
        {
            return _feed;
        }

        var text = File.Exists(FeedPath) ? File.ReadAllText(FeedPath) : "";
        var complete = text.EndsWith('\n') ? text : text[..(text.LastIndexOf('\n') + 1)];
        if (complete.Length != text.Length)
        {
            File.WriteAllText(FeedPath, complete);
        }

        _feed = complete.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<FeedEntry>(line, JsonFile.Options)!)
            .ToList();
        _byPath = _feed.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        return _feed;
    }
}

using System.Text;

namespace Kofre.Server.Tests;

/// <summary>A store on disk, without the API: what a crash may leave behind.</summary>
public sealed class StoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kofre-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Store _store;

    public StoreTests() => _store = Store.Create(Guid.NewGuid(), _folder, Guid.NewGuid(), DateTimeOffset.UtcNow);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task AFileMovedButNotInTheFeed_IsAddedToTheFeed_AtTheNextUpload()
    {
        var file = Path.Combine(_folder, Store.FilesFolder, "a");
        File.WriteAllText(file, "abc");

        var result = await _store.AddAsync("a", Content("other"), 100, CancellationToken.None);

        Assert.Equal(AddResult.Exists, result);
        Assert.Equal([new FeedEntry(1, "a", 3)], _store.FeedSince(0, 10).Entries);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, Store.IncomingFolder)));
    }

    [Fact]
    public async Task AFeedLineCutByACrash_IsDropped()
    {
        await _store.AddAsync("a", Content("a"), 100, CancellationToken.None);
        File.AppendAllText(Path.Combine(_folder, Store.FeedFile), "{\"seq\":2,\"pa");

        var reopened = new Store(_store.Id, _folder);
        await reopened.AddAsync("b", Content("b"), 100, CancellationToken.None);

        Assert.Equal([new FeedEntry(1, "a", 1), new FeedEntry(2, "b", 1)], new Store(_store.Id, _folder).FeedSince(0, 10).Entries);
    }

    [Fact]
    public void AStoreWithoutItsInfo_CannotBeRead()
    {
        File.Delete(Path.Combine(_folder, Store.InfoFile));

        Assert.Throws<InvalidDataException>(() => _store.Info);
    }

    private static MemoryStream Content(string text) => new(Encoding.UTF8.GetBytes(text));
}

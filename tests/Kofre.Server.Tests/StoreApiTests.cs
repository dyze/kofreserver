using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kofre.Server.Storage;
using static Kofre.Server.Tests.AccountTests;

namespace Kofre.Server.Tests;

public sealed class StoreApiTests : IDisposable
{
    private readonly KofreServer _server = new(new() { ["MaxUploadBytes"] = "100" });

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task ACreatedStore_IsListedForItsOwnerOnly()
    {
        var ada = await _server.SignedIn("ada@example.com");
        var bob = await _server.SignedIn("bob@example.com");

        var id = await Create(ada);

        var listed = Assert.Single((await ada.GetFromJsonAsync<List<StoreSummary>>("/v1/stores", JsonFile.Options))!);
        Assert.Equal(new StoreSummary(id, Role.Owner, 0, 0), listed);
        Assert.Empty((await bob.GetFromJsonAsync<List<StoreSummary>>("/v1/stores", JsonFile.Options))!);
        await AssertRefused(await bob.GetAsync($"/v1/stores/{id}/feed"), HttpStatusCode.Forbidden, "not-allowed");
        await AssertRefused(await bob.GetAsync($"/v1/stores/{id}/stats"), HttpStatusCode.Forbidden, "not-allowed");
        await AssertRefused(await bob.PutAsync($"/v1/stores/{id}/files/a", Bytes("x")), HttpStatusCode.Forbidden, "not-allowed");
    }

    [Fact]
    public async Task AnUnknownStore_IsNotFound()
    {
        var ada = await _server.SignedIn();

        await AssertRefused(await ada.GetAsync($"/v1/stores/{Guid.NewGuid()}/feed"), HttpStatusCode.NotFound, "no-store");
    }

    [Fact]
    public async Task AnUploadedFile_IsDownloaded_AndInTheFeed()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);

        var upload = await ada.PutAsync($"/v1/stores/{id}/files/data/3f/3f9c.bin", Bytes("hello"));
        await ada.PutAsync($"/v1/stores/{id}/files/header.json", Bytes("{}"));

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Equal("hello", await ada.GetStringAsync($"/v1/stores/{id}/files/data/3f/3f9c.bin"));
        var feed = (await ada.GetFromJsonAsync<FeedPage>($"/v1/stores/{id}/feed"))!;
        Assert.Equal([new FeedEntry(1, "data/3f/3f9c.bin", 5), new FeedEntry(2, "header.json", 2)], feed.Entries);
        Assert.False(feed.More);
        Assert.Equal(new StoreStats(2, 7), await ada.GetFromJsonAsync<StoreStats>($"/v1/stores/{id}/stats"));
    }

    [Fact]
    public async Task TheFeed_IsReadInPages_FromASequenceNumber()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);
        foreach (var name in new[] { "a", "b", "c" })
        {
            await ada.PutAsync($"/v1/stores/{id}/files/{name}", Bytes(name));
        }

        var page = (await ada.GetFromJsonAsync<FeedPage>($"/v1/stores/{id}/feed?since=1&limit=1"))!;

        Assert.Equal([new FeedEntry(2, "b", 1)], page.Entries);
        Assert.True(page.More);
    }

    [Fact]
    public async Task AFile_IsNeverReplaced()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);
        await ada.PutAsync($"/v1/stores/{id}/files/a", Bytes("first"));

        var again = await ada.PutAsync($"/v1/stores/{id}/files/a", Bytes("second"));

        await AssertRefused(again, HttpStatusCode.Conflict, "file-exists");
        Assert.Equal("first", await ada.GetStringAsync($"/v1/stores/{id}/files/a"));
    }

    [Theory]
    [InlineData("a%20b")]
    [InlineData("a//b")]
    public async Task AnInvalidPath_IsRefused(string path)
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);

        var upload = await ada.PutAsync($"/v1/stores/{id}/files/{path}", Bytes("x"));
        var download = await ada.GetAsync($"/v1/stores/{id}/files/{path}");

        Assert.Contains(upload.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.NotFound });
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task AMissingFile_IsNotFound()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);

        await AssertRefused(await ada.GetAsync($"/v1/stores/{id}/files/a"), HttpStatusCode.NotFound, "no-file");
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefused_WithOrWithoutItsLength()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);
        var large = new string('x', 101);

        var known = await ada.PutAsync($"/v1/stores/{id}/files/a", Bytes(large));
        var streamed = await ada.PutAsync($"/v1/stores/{id}/files/b", new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes(large))));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, known.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, streamed.StatusCode);
        Assert.Equal(new StoreStats(0, 0), await ada.GetFromJsonAsync<StoreStats>($"/v1/stores/{id}/stats"));
    }

    [Fact]
    public async Task AReader_CannotUpload()
    {
        var ada = await _server.SignedIn("ada@example.com");
        var bob = await _server.SignedIn("bob@example.com");
        var id = await Create(ada);
        await ada.PutAsync($"/v1/stores/{id}/files/a", Bytes("a"));
        AddMember(id, _server.Account("bob@example.com").Id, Role.Reader);

        var upload = await bob.PutAsync($"/v1/stores/{id}/files/b", Bytes("b"));

        await AssertRefused(upload, HttpStatusCode.Forbidden, "not-allowed");
        Assert.Equal("a", await bob.GetStringAsync($"/v1/stores/{id}/files/a"));
    }

    [Fact]
    public async Task ADeletedStore_GoesToTheTrash_AndIsRestored()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);
        await ada.PutAsync($"/v1/stores/{id}/files/a", Bytes("a"));

        await AssertRefused(await ada.DeleteAsync($"/v1/stores/{id}"), HttpStatusCode.Forbidden, "wrong-password");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(ada, HttpMethod.Delete, $"/v1/stores/{id}", KofreServer.Password)).StatusCode);

        Assert.Empty((await ada.GetFromJsonAsync<List<StoreSummary>>("/v1/stores", JsonFile.Options))!);
        await AssertRefused(await ada.GetAsync($"/v1/stores/{id}/files/a"), HttpStatusCode.Forbidden, "store-deleted");

        await AssertRefused(await ada.PostAsync($"/v1/stores/{id}/restore", null), HttpStatusCode.Forbidden, "wrong-password");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(ada, HttpMethod.Post, $"/v1/stores/{id}/restore", KofreServer.Password)).StatusCode);
        Assert.Equal("a", await ada.GetStringAsync($"/v1/stores/{id}/files/a"));
    }

    [Fact]
    public async Task Restoring_NeedsAnOwner_AndAStoreInTheTrash()
    {
        var ada = await _server.SignedIn("ada@example.com");
        var bob = await _server.SignedIn("bob@example.com");
        var id = await Create(ada);

        await AssertRefused(await Send(ada, HttpMethod.Post, $"/v1/stores/{id}/restore", KofreServer.Password), HttpStatusCode.Conflict, "not-deleted");
        await AssertRefused(await Send(bob, HttpMethod.Post, $"/v1/stores/{id}/restore", KofreServer.Password), HttpStatusCode.Forbidden, "not-allowed");
        await AssertRefused(await Send(ada, HttpMethod.Post, $"/v1/stores/{Guid.NewGuid()}/restore", KofreServer.Password), HttpStatusCode.NotFound, "no-store");
    }

    [Fact]
    public async Task TheTrash_IsPurgedAfterItsDelay()
    {
        var ada = await _server.SignedIn();
        var kept = await Create(ada);
        var purged = await Create(ada);
        await Send(ada, HttpMethod.Delete, $"/v1/stores/{purged}", KofreServer.Password);
        _server.Clock.Advance(TimeSpan.FromDays(29));
        await SignInAgain(ada);
        await Send(ada, HttpMethod.Delete, $"/v1/stores/{kept}", KofreServer.Password);

        _server.Clock.Advance(TimeSpan.FromDays(2));
        var count = _server.Service<StoreRepository>().PurgeTrash();
        await SignInAgain(ada);

        Assert.Equal(1, count);
        await AssertRefused(await Send(ada, HttpMethod.Post, $"/v1/stores/{purged}/restore", KofreServer.Password), HttpStatusCode.NotFound, "no-store");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(ada, HttpMethod.Post, $"/v1/stores/{kept}/restore", KofreServer.Password)).StatusCode);
    }

    private static async Task<Guid> Create(HttpClient client)
    {
        var response = await client.PostAsync("/v1/stores", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedStore>())!.Id;
    }

    /// <summary>A new access token, after the clock moved past the last one's lifetime.</summary>
    private static async Task SignInAgain(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await KofreServer.SignIn(client, "ada@example.com")).AccessToken);

    private static ByteArrayContent Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private void AddMember(Guid store, Guid account, Role role)
    {
        var path = Path.Combine(_server.DataFolder, StoreRepository.FolderName, store.ToString("N"), Store.InfoFile);
        var info = JsonFile.Read<StoreInfo?>(path, null)!;
        info.Members[account] = role;
        JsonFile.Write(path, info);
    }

    /// <summary>Content sent without a length (chunked), so the server checks the limit while reading.</summary>
    private sealed class UnknownLengthStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kofre.Server.Storage;
using static Kofre.Server.Tests.AccountTests;

namespace Kofre.Server.Tests;

public sealed class MemberApiTests : IDisposable
{
    private readonly KofreServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task TheOwner_AddsAMember_WhoListsTheStore_AndActsByItsRole()
    {
        var (ada, bob, store) = await TwoAccountsAndAStore();

        var added = await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Reader), JsonFile.Options);

        Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
        Assert.Equal(new StoreSummary(store, Role.Reader, 0, 0), Assert.Single((await bob.GetFromJsonAsync<List<StoreSummary>>("/v1/stores", JsonFile.Options))!));
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/v1/stores/{store}/feed")).StatusCode);
        await AssertRefused(await bob.PutAsync($"/v1/stores/{store}/files/a", Bytes("x")), HttpStatusCode.Forbidden, "not-allowed");

        await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Writer), JsonFile.Options);

        Assert.Equal(HttpStatusCode.Created, (await bob.PutAsync($"/v1/stores/{store}/files/a", Bytes("x"))).StatusCode);
    }

    [Fact]
    public async Task EveryMember_ReadsTheMembers()
    {
        var (ada, bob, store) = await TwoAccountsAndAStore();
        await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Reader), JsonFile.Options);

        var members = (await bob.GetFromJsonAsync<List<Member>>($"/v1/stores/{store}/members", JsonFile.Options))!;

        Assert.Equal([new Member(Ada, Role.Owner), new Member(Bob, Role.Reader)], members.OrderBy(member => member.Role == Role.Owner ? 0 : 1));
    }

    [Fact]
    public async Task OnlyTheOwner_ChangesTheMembers()
    {
        var (ada, bob, store) = await TwoAccountsAndAStore();
        await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Writer), JsonFile.Options);

        await AssertRefused(await bob.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Reader), JsonFile.Options), HttpStatusCode.Forbidden, "not-allowed");
        await AssertRefused(await bob.DeleteAsync($"/v1/stores/{store}/members/{Ada}"), HttpStatusCode.Forbidden, "not-allowed");
        await AssertRefused(await bob.PutAsJsonAsync($"/v1/stores/{store}/fence", new FenceValue(1)), HttpStatusCode.Forbidden, "not-allowed");
    }

    [Fact]
    public async Task TheOwner_IsNeitherChangedNorRemoved_NorAddedAgain()
    {
        var (ada, _, store) = await TwoAccountsAndAStore();

        await AssertRefused(await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Ada}", new MemberRole(Role.Reader), JsonFile.Options), HttpStatusCode.Conflict, "owner-fixed");
        await AssertRefused(await ada.DeleteAsync($"/v1/stores/{store}/members/{Ada}"), HttpStatusCode.Conflict, "owner-fixed");
        await AssertRefused(await ada.DeleteAsync($"/v1/stores/{store}/members/me"), HttpStatusCode.Conflict, "owner-cannot-leave");
        await AssertRefused(await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Owner), JsonFile.Options), HttpStatusCode.BadRequest, "invalid-role");
    }

    [Fact]
    public async Task OnlyAnActiveAccount_IsAdded()
    {
        var (ada, _, store) = await TwoAccountsAndAStore();
        _server.Edit("bob@example.com", account => account with { Disabled = true });

        await AssertRefused(await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Reader), JsonFile.Options), HttpStatusCode.NotFound, "no-account");
        await AssertRefused(await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Guid.NewGuid()}", new MemberRole(Role.Reader), JsonFile.Options), HttpStatusCode.NotFound, "no-account");
    }

    [Fact]
    public async Task ARemovedMember_IsRefusedAtOnce()
    {
        var (ada, bob, store) = await TwoAccountsAndAStore();
        await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Writer), JsonFile.Options);

        Assert.Equal(HttpStatusCode.NoContent, (await ada.DeleteAsync($"/v1/stores/{store}/members/{Bob}")).StatusCode);

        await AssertRefused(await bob.GetAsync($"/v1/stores/{store}/feed"), HttpStatusCode.Forbidden, "not-allowed");
        Assert.Empty((await bob.GetFromJsonAsync<List<StoreSummary>>("/v1/stores", JsonFile.Options))!);
        await AssertRefused(await ada.DeleteAsync($"/v1/stores/{store}/members/{Bob}"), HttpStatusCode.NotFound, "not-member");
    }

    [Fact]
    public async Task AMember_Leaves()
    {
        var (ada, bob, store) = await TwoAccountsAndAStore();
        await ada.PutAsJsonAsync($"/v1/stores/{store}/members/{Bob}", new MemberRole(Role.Reader), JsonFile.Options);

        Assert.Equal(HttpStatusCode.NoContent, (await bob.DeleteAsync($"/v1/stores/{store}/members/me")).StatusCode);

        await AssertRefused(await bob.GetAsync($"/v1/stores/{store}/members"), HttpStatusCode.Forbidden, "not-allowed");
        Assert.Equal([new Member(Ada, Role.Owner)], (await ada.GetFromJsonAsync<List<Member>>($"/v1/stores/{store}/members", JsonFile.Options))!);
    }

    [Fact]
    public async Task OnceTheFenceIsOn_AnUploadNamesANumberAtLeastAsHigh()
    {
        var (ada, _, store) = await TwoAccountsAndAStore();

        Assert.Equal(HttpStatusCode.Created, (await Upload(ada, store, "free", fence: null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ada.PutAsJsonAsync($"/v1/stores/{store}/fence", new FenceValue(3))).StatusCode);

        await AssertFenced(await Upload(ada, store, "none", fence: null), "fence", 3);
        await AssertFenced(await Upload(ada, store, "low", fence: "2"), "fence", 3);
        await AssertFenced(await Upload(ada, store, "junk", fence: "x"), "fence", 3);
        Assert.Equal(HttpStatusCode.Created, (await Upload(ada, store, "even", fence: "3")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Upload(ada, store, "above", fence: "4")).StatusCode);
        Assert.Equal(3, Assert.Single((await ada.GetFromJsonAsync<List<StoreSummary>>("/v1/stores", JsonFile.Options))!).Fence);
    }

    [Fact]
    public async Task TheFence_IsRaised_NeverLowered_AndTurnedOff()
    {
        var (ada, _, store) = await TwoAccountsAndAStore();
        await ada.PutAsJsonAsync($"/v1/stores/{store}/fence", new FenceValue(3));

        Assert.Equal(HttpStatusCode.NoContent, (await ada.PutAsJsonAsync($"/v1/stores/{store}/fence", new FenceValue(3))).StatusCode);
        await AssertFenced(await ada.PutAsJsonAsync($"/v1/stores/{store}/fence", new FenceValue(2)), "fence-lower", 3);
        await AssertRefused(await ada.PutAsJsonAsync($"/v1/stores/{store}/fence", new FenceValue(-1)), HttpStatusCode.BadRequest, "invalid-fence");

        Assert.Equal(HttpStatusCode.NoContent, (await ada.DeleteAsync($"/v1/stores/{store}/fence")).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await Upload(ada, store, "a", fence: null)).StatusCode);
    }

    [Fact]
    public async Task AFenceRaisedDuringAnUpload_RefusesIt()
    {
        var (ada, _, store) = await TwoAccountsAndAStore();
        var stored = _server.Service<StoreRepository>().Find(store)!;
        stored.SetFence(1);

        using var content = new RaisingStream(Encoding.UTF8.GetBytes("x"), () => stored.SetFence(2));
        var result = await stored.AddAsync("a", content, 100, fence: 1, CancellationToken.None);

        Assert.Equal(AddResult.Fenced, result);
        Assert.Null(stored.FileOf("a"));
    }

    [Fact]
    public async Task MembersAndFence_OfAStoreInTheTrash_OrUnknown_AreRefused()
    {
        var (ada, _, store) = await TwoAccountsAndAStore();
        _server.Service<StoreRepository>().Find(store)!.SetDeleted(_server.Clock.Now);

        await AssertRefused(await ada.GetAsync($"/v1/stores/{store}/members"), HttpStatusCode.Forbidden, "store-deleted");
        await AssertRefused(await ada.DeleteAsync($"/v1/stores/{store}/members/me"), HttpStatusCode.Forbidden, "store-deleted");
        await AssertRefused(await ada.DeleteAsync($"/v1/stores/{store}/fence"), HttpStatusCode.Forbidden, "store-deleted");
        await AssertRefused(await ada.DeleteAsync($"/v1/stores/{Guid.NewGuid()}/members/{Bob}"), HttpStatusCode.NotFound, "no-store");
    }

    private Guid Ada => _server.Account("ada@example.com").Id;

    private Guid Bob => _server.Account("bob@example.com").Id;

    private async Task<(HttpClient Ada, HttpClient Bob, Guid Store)> TwoAccountsAndAStore()
    {
        var ada = await _server.SignedIn("ada@example.com");
        var bob = await _server.SignedIn("bob@example.com");
        var response = await ada.PostAsync("/v1/stores", null);
        return (ada, bob, (await response.Content.ReadFromJsonAsync<CreatedStore>())!.Id);
    }

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid store, string path, string? fence)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/v1/stores/{store}/files/{path}") { Content = Bytes(path) };
        if (fence is not null)
        {
            request.Headers.Add(StoreEndpoints.FenceHeader, fence);
        }

        return client.SendAsync(request);
    }

    private static async Task AssertFenced(HttpResponseMessage response, string error, long fence)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new FenceRefusal(error, fence), await response.Content.ReadFromJsonAsync<FenceRefusal>());
    }

    private static ByteArrayContent Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    /// <summary>Content that runs <paramref name="atEnd"/> once read to its end: something happening during an upload.</summary>
    private sealed class RaisingStream(byte[] content, Action atEnd) : MemoryStream(content)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
        {
            var read = base.ReadAsync(buffer, cancel);
            if (Position == Length)
            {
                atEnd();
            }

            return read;
        }
    }
}

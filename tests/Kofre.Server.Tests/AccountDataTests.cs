using System.Net;
using System.Net.Http.Json;
using static Kofre.Server.Tests.AccountTests;

namespace Kofre.Server.Tests;

public sealed class AccountDataTests : IDisposable
{
    private static readonly byte[] PublicPart = [1, 2, 3];
    private static readonly byte[] PrivatePart = [4, 5, 6];

    private readonly KofreServer _server = new(new() { ["MaxAccountDataBytes"] = "10" });

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task AnAccount_HasNoData_AtFirst()
    {
        var ada = await _server.SignedIn();

        var data = (await ada.GetFromJsonAsync<AccountData>("/v1/accounts/me/data"))!;

        Assert.Equal(0, data.Version);
        Assert.Null(data.Public);
        Assert.Null(data.Private);
    }

    [Fact]
    public async Task WrittenData_IsReadBack_WithItsNextVersion()
    {
        var ada = await _server.SignedIn();

        var written = await ada.PutAsJsonAsync("/v1/accounts/me/data", new DataUpdate(0, PublicPart, PrivatePart));

        Assert.Equal(1, (await written.Content.ReadFromJsonAsync<DataVersion>())!.Version);
        var data = (await ada.GetFromJsonAsync<AccountData>("/v1/accounts/me/data"))!;
        Assert.Equal(1, data.Version);
        Assert.Equal(PublicPart, data.Public);
        Assert.Equal(PrivatePart, data.Private);
        Assert.Equal(_server.Clock.Now, data.Updated);
    }

    [Fact]
    public async Task AWriteOverAnotherVersion_IsRefused_WithTheCurrentOne()
    {
        var ada = await _server.SignedIn();
        await ada.PutAsJsonAsync("/v1/accounts/me/data", new DataUpdate(0, PublicPart, PrivatePart));

        var late = await ada.PutAsJsonAsync("/v1/accounts/me/data", new DataUpdate(0, [9], [9]));

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal(new DataConflict("data-changed", 1), await late.Content.ReadFromJsonAsync<DataConflict>());
        Assert.Equal(PublicPart, (await ada.GetFromJsonAsync<AccountData>("/v1/accounts/me/data"))!.Public);
    }

    [Fact]
    public async Task MissingOrTooLargeParts_AreRefused()
    {
        var ada = await _server.SignedIn();

        await AssertRefused(await ada.PutAsJsonAsync("/v1/accounts/me/data", new DataUpdate(0, PublicPart, null)), HttpStatusCode.BadRequest, "invalid-data");
        await AssertRefused(await ada.PutAsJsonAsync("/v1/accounts/me/data", new DataUpdate(0, new byte[11], PrivatePart)), HttpStatusCode.RequestEntityTooLarge, "too-large");
    }

    [Fact]
    public async Task AnActiveAccount_IsFoundByItsExactEmail_WithItsPublicPart_NeverItsPrivatePart()
    {
        var ada = await _server.SignedIn("ada@example.com");
        var bob = await _server.SignedIn("bob@example.com");
        await bob.PutAsJsonAsync("/v1/accounts/me/data", new DataUpdate(0, PublicPart, PrivatePart));
        var bobId = _server.Account("bob@example.com").Id;

        var found = await ada.GetAsync("/v1/accounts?email=BOB@example.com");

        var account = (await found.Content.ReadFromJsonAsync<FoundAccount>())!;
        Assert.Equal(bobId, account.Id);
        Assert.Equal(PublicPart, account.Public);
        Assert.DoesNotContain(Convert.ToBase64String(PrivatePart), await found.Content.ReadAsStringAsync());
        Assert.Equal(PublicPart, (await ada.GetFromJsonAsync<PublicPart>($"/v1/accounts/{bobId}/public"))!.Public);
        await AssertRefused(await ada.GetAsync("/v1/accounts?email=bob@example"), HttpStatusCode.NotFound, "no-account");
        await AssertRefused(await ada.GetAsync("/v1/accounts"), HttpStatusCode.NotFound, "no-account");
    }

    [Fact]
    public async Task AnAccountNotActive_IsNotFound()
    {
        var ada = await _server.SignedIn("ada@example.com");
        await _server.SignedIn("bob@example.com");
        var bobId = _server.Account("bob@example.com").Id;
        _server.Edit("bob@example.com", account => account with { Disabled = true });

        await AssertRefused(await ada.GetAsync("/v1/accounts?email=bob@example.com"), HttpStatusCode.NotFound, "no-account");
        await AssertRefused(await ada.GetAsync($"/v1/accounts/{bobId}/public"), HttpStatusCode.NotFound, "no-account");
        await AssertRefused(await ada.GetAsync($"/v1/accounts/{Guid.NewGuid()}/public"), HttpStatusCode.NotFound, "no-account");
    }

    [Fact]
    public async Task Lookups_AreLimitedPerAccount()
    {
        using var server = new KofreServer(new() { ["LookupsPerMinute"] = "2" });
        var ada = await server.SignedIn("ada@example.com");
        var bob = await server.SignedIn("bob@example.com");

        await ada.GetAsync("/v1/accounts?email=x@example.com");
        await ada.GetAsync("/v1/accounts?email=y@example.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await ada.GetAsync("/v1/accounts?email=z@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync("/v1/accounts?email=z@example.com")).StatusCode);
    }

    [Fact]
    public async Task AccountData_NeedsASignedInAccount()
    {
        var client = _server.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/accounts/me/data")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/accounts?email=a@example.com")).StatusCode);
    }

    [Fact]
    public async Task TheVersion_IsTheAssemblys_WithoutSigningIn()
    {
        var version = (await _server.CreateClient().GetFromJsonAsync<ServerVersion>("/v1/version"))!;

        Assert.Matches(@"^\d+\.\d+$", version.Version);
        Assert.Equal(typeof(KofreOptions).Assembly.GetName().Version!.ToString(2), version.Version);
    }
}

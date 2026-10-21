using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Tests;

public sealed class AccountTests : IDisposable
{
    private const string Email = "ada@example.com";

    private readonly KofreServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task ANewAccount_WaitsForTheAdminsApproval()
    {
        var client = _server.CreateClient();

        var signUp = await client.PostAsJsonAsync("/v1/accounts", new Credentials(Email, KofreServer.Password));
        var signIn = await client.PostAsJsonAsync("/v1/sessions", new Credentials(Email, KofreServer.Password));

        Assert.Equal(HttpStatusCode.Accepted, signUp.StatusCode);
        Assert.Equal((await signUp.Content.ReadFromJsonAsync<SignedUp>())!.Id, _server.Account(Email).Id);
        await AssertRefused(signIn, HttpStatusCode.Forbidden, "account-not-approved");
    }

    [Fact]
    public async Task AccountsWaitingForApproval_AreLimited_ApprovedAndDisabledOnesNotCounted()
    {
        using var server = new KofreServer(new() { ["MaxPendingAccounts"] = "2" });
        var client = server.CreateClient();
        async Task<HttpResponseMessage> SignUp(string email) => await client.PostAsJsonAsync("/v1/accounts", new Credentials(email, KofreServer.Password));

        Assert.Equal(HttpStatusCode.Accepted, (await SignUp("a@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SignUp("b@example.com")).StatusCode);
        await AssertRefused(await SignUp("c@example.com"), HttpStatusCode.Forbidden, "sign-up-closed");

        server.Edit("a@example.com", account => account with { Approved = true });
        server.Edit("b@example.com", account => account with { Disabled = true });

        Assert.Equal(HttpStatusCode.Accepted, (await SignUp("c@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await SignUp("d@example.com")).StatusCode);
        await AssertRefused(await SignUp("e@example.com"), HttpStatusCode.Forbidden, "sign-up-closed");
    }

    [Fact]
    public async Task NoAccountWaiting_ClosesSignUps()
    {
        using var server = new KofreServer(new() { ["MaxPendingAccounts"] = "0" });

        var response = await server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials(Email, KofreServer.Password));

        await AssertRefused(response, HttpStatusCode.Forbidden, "sign-up-closed");
        Assert.False(File.Exists(server.AccountsFile));
    }

    [Fact]
    public async Task OnlyTheAdminsFields_AreWritten()
    {
        await _server.SignedIn(Email);

        var file = File.ReadAllText(_server.AccountsFile);

        Assert.Contains("\"approved\": true", file);
        Assert.DoesNotContain("isActive", file);
        Assert.DoesNotContain("isPending", file);
    }

    [Fact]
    public async Task ThePassword_IsKeptHashed()
    {
        await _server.SignedIn(Email);

        Assert.DoesNotContain(KofreServer.Password, File.ReadAllText(_server.AccountsFile));
        Assert.StartsWith("pbkdf2-sha256$10$", _server.Account(Email).PasswordHash);
    }

    [Theory]
    [InlineData("", "invalid-email")]
    [InlineData("ada", "invalid-email")]
    [InlineData("@example.com", "invalid-email")]
    [InlineData("ada@", "invalid-email")]
    [InlineData("ada lovelace@example.com", "invalid-email")]
    public async Task AnInvalidEmail_IsRefused(string email, string error)
    {
        var response = await _server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials(email, KofreServer.Password));

        await AssertRefused(response, HttpStatusCode.BadRequest, error);
    }

    [Fact]
    public async Task AShortPassword_IsRefused()
    {
        var response = await _server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials(Email, "short"));

        await AssertRefused(response, HttpStatusCode.BadRequest, "weak-password");
    }

    [Fact]
    public async Task AnEmailTaken_InAnyCase_IsRefused()
    {
        await _server.SignedIn(Email);

        var response = await _server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials(" ADA@example.com ", KofreServer.Password));

        await AssertRefused(response, HttpStatusCode.Conflict, "email-taken");
    }

    [Theory]
    [InlineData(Email, "wrong password!")]
    [InlineData("bob@example.com", KofreServer.Password)]
    public async Task AWrongPasswordOrUnknownEmail_IsRefusedAlike(string email, string password)
    {
        await _server.SignedIn(Email);

        var response = await _server.CreateClient().PostAsJsonAsync("/v1/sessions", new Credentials(email, password));

        await AssertRefused(response, HttpStatusCode.Unauthorized, "wrong-credentials");
    }

    [Fact]
    public async Task ADisabledAccount_IsRefusedAtOnce_EvenWithAValidToken()
    {
        var client = await _server.SignedIn(Email);

        _server.Edit(Email, account => account with { Disabled = true });

        await AssertRefused(await client.GetAsync("/v1/stores"), HttpStatusCode.Forbidden, "account-inactive");
        await AssertRefused(await client.PostAsJsonAsync("/v1/sessions", new Credentials(Email, KofreServer.Password)), HttpStatusCode.Forbidden, "account-disabled");
    }

    [Fact]
    public async Task WithoutAToken_TheApiIsRefused()
    {
        var response = await _server.CreateClient().GetAsync("/v1/stores");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAccessToken_ExpiresAfterItsLifetime()
    {
        var client = await _server.SignedIn(Email);

        _server.Clock.Advance(TimeSpan.FromMinutes(16));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/stores")).StatusCode);
    }

    [Fact]
    public async Task ARefreshToken_GivesANewSession_AndIsUsedOnce()
    {
        var client = await _server.SignedIn(Email);
        var session = await KofreServer.SignIn(client, Email);

        var first = await client.PostAsJsonAsync("/v1/sessions/refresh", new RefreshRequest(session.RefreshToken));
        var again = await client.PostAsJsonAsync("/v1/sessions/refresh", new RefreshRequest(session.RefreshToken));

        var renewed = (await first.Content.ReadFromJsonAsync<Session>())!;
        Assert.NotEqual(session.RefreshToken, renewed.RefreshToken);
        Assert.DoesNotContain(session.RefreshToken, File.ReadAllText(Path.Combine(_server.DataFolder, TokenService.FileName)));
        await AssertRefused(again, HttpStatusCode.Unauthorized, "session-ended");
    }

    [Fact]
    public async Task ARefreshToken_Expires()
    {
        var client = await _server.SignedIn(Email);
        var session = await KofreServer.SignIn(client, Email);

        _server.Clock.Advance(TimeSpan.FromDays(31));
        var response = await client.PostAsJsonAsync("/v1/sessions/refresh", new RefreshRequest(session.RefreshToken));

        await AssertRefused(response, HttpStatusCode.Unauthorized, "session-ended");
    }

    [Fact]
    public async Task ARefresh_OfADisabledAccount_IsRefused()
    {
        var client = await _server.SignedIn(Email);
        var session = await KofreServer.SignIn(client, Email);

        _server.Edit(Email, account => account with { Disabled = true });
        var response = await client.PostAsJsonAsync("/v1/sessions/refresh", new RefreshRequest(session.RefreshToken));

        await AssertRefused(response, HttpStatusCode.Forbidden, "account-inactive");
    }

    [Fact]
    public async Task ChangingThePassword_NeedsTheCurrentOne()
    {
        var client = await _server.SignedIn(Email);

        var without = await client.PutAsJsonAsync("/v1/accounts/me/password", new NewPassword("another long password"));
        var weak = await Send(client, HttpMethod.Put, "/v1/accounts/me/password", KofreServer.Password, new NewPassword("short"));

        await AssertRefused(without, HttpStatusCode.Forbidden, "wrong-password");
        await AssertRefused(weak, HttpStatusCode.BadRequest, "weak-password");
    }

    [Fact]
    public async Task ChangingThePassword_EndsTheOtherSessions()
    {
        var client = await _server.SignedIn(Email);
        var other = await KofreServer.SignIn(client, Email);

        var response = await Send(client, HttpMethod.Put, "/v1/accounts/me/password", KofreServer.Password, new NewPassword("another long password"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/sessions/refresh", new RefreshRequest(other.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/sessions", new Credentials(Email, KofreServer.Password))).StatusCode);
        await KofreServer.SignIn(client, Email, "another long password");
    }

    [Fact]
    public async Task SignIns_AreLimitedPerMinute()
    {
        using var server = new KofreServer(new() { ["SignInsPerMinute"] = "2" });
        var client = server.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            statuses.Add((await client.PostAsJsonAsync("/v1/sessions", new Credentials(Email, KofreServer.Password))).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public void WithoutASigningKey_TheServerDoesNotStart()
    {
        using var server = new KofreServer(new() { ["SigningKey"] = "c2hvcnQ=" });

        var error = Assert.Throws<OptionsValidationException>(() => server.CreateClient());

        Assert.Contains(KofreOptions.SigningKeyRule, error.Message);
    }

    internal static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string url, string password, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(Caller.PasswordHeader, password);
        return await client.SendAsync(request);
    }

    internal static async Task AssertRefused(HttpResponseMessage response, HttpStatusCode status, string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(error, (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
    }
}

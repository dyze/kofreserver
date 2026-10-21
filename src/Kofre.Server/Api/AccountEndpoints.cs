using System.Collections.Concurrent;
using Kofre.Server.Accounts;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Api;

public sealed record Credentials(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record NewPassword(string Password);

public sealed record SignedUp(Guid Id);

/// <summary>Sign up, sign in, refresh, change password.</summary>
internal static class AccountEndpoints
{
    public const string SignInLimit = "sign-in";

    private const int MinPasswordLength = 10;
    private const int MaxPasswordLength = 1024;
    private const int MaxEmailLength = 254;

    private static readonly ConcurrentDictionary<int, string> UnknownAccountHashes = new();

    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/accounts", SignUp).RequireRateLimiting(SignInLimit);
        api.MapPost("/sessions", SignIn).RequireRateLimiting(SignInLimit);
        api.MapPost("/sessions/refresh", Refresh).RequireRateLimiting(SignInLimit);
        api.MapPut("/accounts/me/password", ChangePassword).RequireAuthorization().AddEndpointFilter(Caller.RequireActive);
    }

    private static IResult SignUp(Credentials credentials, AccountStore accounts, IOptions<KofreOptions> options, TimeProvider clock, ILogger<Account> log)
    {
        var email = credentials.Email?.Trim() ?? "";
        if (!IsEmail(email))
        {
            return Errors.BadRequest("invalid-email");
        }

        if (!IsStrong(credentials.Password))
        {
            return Errors.BadRequest("weak-password");
        }

        var signUp = accounts.Add(email, PasswordHash.Create(credentials.Password, options.Value.PasswordIterations), clock.GetUtcNow());
        if (signUp.Refused == SignUpRefusal.TooManyPending)
        {
            log.LogWarning("Sign-up refused: {Limit} accounts wait for approval already (MaxPendingAccounts)", options.Value.MaxPendingAccounts);
            return Errors.Forbidden("sign-up-closed");
        }

        if (signUp.Added is not { } account)
        {
            return Errors.Conflict("email-taken");
        }

        log.LogInformation("Account {Account} signed up, waiting for approval", account.Id);
        return Results.Accepted(value: new SignedUp(account.Id));
    }

    private static IResult SignIn(Credentials credentials, AccountStore accounts, TokenService tokens, IOptions<KofreOptions> options, ILogger<Account> log)
    {
        var account = accounts.FindByEmail(credentials.Email?.Trim() ?? "");
        // An unknown email costs the same time as a wrong password: no hint that it exists.
        var iterations = options.Value.PasswordIterations;
        var hash = account?.PasswordHash ?? UnknownAccountHashes.GetOrAdd(iterations, _ => PasswordHash.Create(Guid.NewGuid().ToString(), iterations));
        if (!PasswordHash.Verify(credentials.Password ?? "", hash) || account is null)
        {
            log.LogWarning("Sign-in refused: unknown email or wrong password");
            return Errors.Unauthorized("wrong-credentials");
        }

        if (!account.IsActive)
        {
            log.LogInformation("Sign-in of {Account} refused: {State}", account.Id, account.Disabled ? "disabled" : "not approved");
            return Errors.Forbidden(account.Disabled ? "account-disabled" : "account-not-approved");
        }

        log.LogInformation("Account {Account} signed in", account.Id);
        return Results.Ok(tokens.Issue(account.Id));
    }

    private static IResult Refresh(RefreshRequest request, AccountStore accounts, TokenService tokens, ILogger<Account> log)
    {
        if (tokens.Redeem(request.RefreshToken ?? "") is not { } id)
        {
            return Errors.Unauthorized("session-ended");
        }

        if (accounts.Find(id) is not { IsActive: true })
        {
            return Errors.Forbidden("account-inactive");
        }

        log.LogDebug("Session of {Account} refreshed", id);
        return Results.Ok(tokens.Issue(id));
    }

    private static IResult ChangePassword(NewPassword request, HttpContext context, AccountStore accounts, TokenService tokens, IOptions<KofreOptions> options, ILogger<Account> log)
    {
        if (!Caller.RepeatsPassword(context))
        {
            return Errors.Forbidden("wrong-password");
        }

        if (!IsStrong(request.Password))
        {
            return Errors.BadRequest("weak-password");
        }

        var account = Caller.Of(context);
        accounts.SetPasswordHash(account.Id, PasswordHash.Create(request.Password, options.Value.PasswordIterations));
        tokens.RevokeAll(account.Id);
        log.LogInformation("Password of {Account} changed, its sessions ended", account.Id);
        return Results.Ok(tokens.Issue(account.Id));
    }

    private static bool IsEmail(string email) =>
        email.Length <= MaxEmailLength && email.IndexOf('@') is > 0 and var at && at < email.Length - 1 && !email.Any(char.IsWhiteSpace);

    private static bool IsStrong(string? password) => password is { Length: >= MinPasswordLength and <= MaxPasswordLength };
}

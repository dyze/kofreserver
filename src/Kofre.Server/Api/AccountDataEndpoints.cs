using System.Security.Claims;
using Kofre.Server.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kofre.Server.Api;

public sealed record DataUpdate(long Version, byte[]? Public, byte[]? Private);

public sealed record DataVersion(long Version);

public sealed record DataConflict(string Error, long Version);

public sealed record FoundAccount(Guid Id, byte[]? Public);

public sealed record PublicPart(byte[]? Public);

/// <summary>
/// An account's data (its own, read and written), and other accounts: found by exact email, their
/// public part read. Only active accounts are found; the others answer as unknown ones.
/// </summary>
internal static class AccountDataEndpoints
{
    public const string LookupLimit = "lookup";

    public static void Map(RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts").RequireAuthorization().AddEndpointFilter(Caller.RequireActive);
        accounts.MapGet("/me/data", Read);
        accounts.MapPut("/me/data", Write);
        accounts.MapGet("/", Find).RequireRateLimiting(LookupLimit);
        accounts.MapGet("/{id:guid}/public", PublicOf);
    }

    /// <summary>The partition of <see cref="LookupLimit"/>: the signed-in account.</summary>
    public static string LookupPartition(HttpContext context) => context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? "";

    private static IResult Read(HttpContext context, AccountDataStore data) => Results.Ok(data.Read(Caller.Of(context).Id));

    private static IResult Write(DataUpdate update, HttpContext context, AccountDataStore data, IOptions<KofreOptions> options, TimeProvider clock, ILogger<Account> log)
    {
        if (update.Public is null || update.Private is null)
        {
            return Errors.BadRequest("invalid-data");
        }

        var max = options.Value.MaxAccountDataBytes;
        if (update.Public.Length > max || update.Private.Length > max)
        {
            return Errors.TooLarge();
        }

        var account = Caller.Of(context).Id;
        var (kept, written) = data.Write(account, update.Version, update.Public, update.Private, clock.GetUtcNow());
        if (!written)
        {
            return Results.Json(new DataConflict("data-changed", kept.Version), statusCode: StatusCodes.Status409Conflict);
        }

        log.LogInformation("Data of {Account} written, version {Version}", account, kept.Version);
        return Results.Ok(new DataVersion(kept.Version));
    }

    private static IResult Find(string? email, HttpContext context, AccountStore accounts, AccountDataStore data, ILogger<Account> log)
    {
        var found = accounts.FindByEmail(email?.Trim() ?? "") is { IsActive: true } account ? account : null;
        log.LogInformation("{Account} looked up an account by email: {Result}", Caller.Of(context).Id, found is null ? "none" : found.Id.ToString());
        return found is null ? Errors.NotFound("no-account") : Results.Ok(new FoundAccount(found.Id, data.Read(found.Id).Public));
    }

    private static IResult PublicOf(Guid id, AccountStore accounts, AccountDataStore data) =>
        accounts.Find(id) is { IsActive: true } ? Results.Ok(new PublicPart(data.Read(id).Public)) : Errors.NotFound("no-account");
}

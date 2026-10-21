using System.Security.Claims;
using Kofre.Server.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kofre.Server.Api;

/// <summary>The account making an authenticated request, checked again at every request.</summary>
internal static class Caller
{
    public const string PasswordHeader = "X-Password";

    private const string AccountKey = "kofre.account";

    /// <summary>The caller's account, set by <see cref="RequireActive"/>.</summary>
    public static Account Of(HttpContext context) => (Account)context.Items[AccountKey]!;

    /// <summary>
    /// Refuses a token whose account was disabled (or removed) since it was issued, so that takes
    /// effect at once, not when the token expires.
    /// </summary>
    public static async ValueTask<object?> RequireActive(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var subject = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var accounts = context.RequestServices.GetRequiredService<AccountStore>();
        if (!Guid.TryParse(subject, out var id) || accounts.Find(id) is not { IsActive: true } account)
        {
            return Errors.Forbidden("account-inactive");
        }

        context.Items[AccountKey] = account;
        return await next(invocation);
    }

    /// <summary>Whether the request repeats the caller's password (destructive calls), so a stolen token alone is not enough.</summary>
    public static bool RepeatsPassword(HttpContext context) =>
        context.Request.Headers[PasswordHeader].ToString() is { Length: > 0 } password && PasswordHash.Verify(password, Of(context).PasswordHash);

    public static KofreOptions Options(HttpContext context) => context.RequestServices.GetRequiredService<IOptions<KofreOptions>>().Value;
}

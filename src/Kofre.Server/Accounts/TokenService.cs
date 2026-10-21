using System.Security.Claims;
using System.Security.Cryptography;
using Kofre.Server.Storage;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Kofre.Server.Accounts;

/// <summary>What a client gets when it signs in or refreshes.</summary>
public sealed record Session(string AccessToken, DateTimeOffset AccessExpires, string RefreshToken);

/// <summary>
/// Issues access tokens (JWT: the account id only, short-lived) and refresh tokens (random,
/// kept hashed in <c>tokens.json</c>, used once: each refresh replaces it).
/// </summary>
internal sealed class TokenService(IOptions<KofreOptions> options, TimeProvider clock)
{
    public const string Issuer = "kofre";
    public const string FileName = "tokens.json";

    private readonly Lock _lock = new();

    private string FilePath => Path.Combine(options.Value.DataFolder, FileName);

    public static SymmetricSecurityKey SigningKey(KofreOptions options) => new(options.SigningKeyBytes());

    public Session Issue(Guid account)
    {
        var now = clock.GetUtcNow();
        var expires = now + options.Value.AccessTokenLifetime;
        var access = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, account.ToString())]),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(options.Value), SecurityAlgorithms.HmacSha256),
        });

        var refresh = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        Change(tokens => tokens.Add(new StoredToken(HashOf(refresh), account, now + options.Value.RefreshTokenLifetime)));
        return new Session(access, expires, refresh);
    }

    /// <summary>The account of a valid refresh token, which is used up; null for an unknown or expired one.</summary>
    public Guid? Redeem(string refreshToken)
    {
        var hash = HashOf(refreshToken);
        Guid? account = null;
        Change(tokens =>
        {
            if (tokens.Find(token => token.Hash == hash) is { } found)
            {
                tokens.Remove(found);
                account = found.Account;
            }
        });
        return account;
    }

    /// <summary>Ends every session of an account (password changed).</summary>
    public void RevokeAll(Guid account) => Change(tokens => tokens.RemoveAll(token => token.Account == account));

    /// <summary>Applies a change to the stored tokens, dropping the expired ones.</summary>
    private void Change(Action<List<StoredToken>> change)
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            var tokens = JsonFile.Read<List<StoredToken>>(FilePath, []);
            tokens.RemoveAll(token => token.Expires <= now);
            change(tokens);
            JsonFile.Write(FilePath, tokens);
        }
    }

    private static string HashOf(string token) => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

    private sealed record StoredToken(string Hash, Guid Account, DateTimeOffset Expires);
}

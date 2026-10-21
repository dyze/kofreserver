namespace Kofre.Server;

/// <summary>The server's settings: the <c>Kofre</c> section of its configuration (appsettings.json, environment).</summary>
public sealed class KofreOptions
{
    public const string Section = "Kofre";

    /// <summary>Where accounts, tokens and stores are kept.</summary>
    public string DataFolder { get; set; } = "data";

    /// <summary>The key signing access tokens: base64, at least 32 bytes. Required.</summary>
    public string SigningKey { get; set; } = "";

    public int PasswordIterations { get; set; } = 600_000;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long a deleted store stays in the trash before it is purged.</summary>
    public int TrashDays { get; set; } = 30;

    public long MaxUploadBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>Sign-up, sign-in and refresh requests allowed per minute and client address.</summary>
    public int SignInsPerMinute { get; set; } = 10;

    public const string SigningKeyRule = $"{Section}:{nameof(SigningKey)} must be at least 32 bytes in base64";

    private const int MinSigningKeyBytes = 32;

    public bool HasValidSigningKey => DecodeSigningKey() is not null;

    /// <summary>The signing key's bytes.</summary>
    /// <exception cref="InvalidOperationException">It is missing, not base64, or shorter than 32 bytes.</exception>
    public byte[] SigningKeyBytes() => DecodeSigningKey() ?? throw new InvalidOperationException(SigningKeyRule);

    private byte[]? DecodeSigningKey()
    {
        var key = new byte[SigningKey.Length];
        return Convert.TryFromBase64String(SigningKey, key, out var length) && length >= MinSigningKeyBytes ? key[..length] : null;
    }
}

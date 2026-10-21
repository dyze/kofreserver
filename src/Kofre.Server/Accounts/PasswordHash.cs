using System.Security.Cryptography;

namespace Kofre.Server.Accounts;

/// <summary>Passwords kept as <c>pbkdf2-sha256$iterations$salt$hash</c>, never in clear.</summary>
internal static class PasswordHash
{
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Create(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, iterations);
        return $"{Scheme}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Whether <paramref name="password"/> matches; false for a hash it cannot read.</summary>
    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(parts[3]);
            return CryptographicOperations.FixedTimeEquals(expected, Derive(password, Convert.FromBase64String(parts[2]), iterations));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}

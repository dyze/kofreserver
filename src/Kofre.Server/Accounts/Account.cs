namespace Kofre.Server.Accounts;

/// <summary>
/// A user of the server, as kept in <c>accounts.json</c>. The admin approves it by setting
/// <see cref="Approved"/>, and can disable it.
/// </summary>
public sealed record Account(Guid Id, string Email, string PasswordHash, DateTimeOffset Created, bool Approved = false, bool Disabled = false)
{
    /// <summary>Whether it may sign in and use the API.</summary>
    public bool IsActive => Approved && !Disabled;
}

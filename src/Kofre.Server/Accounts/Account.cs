using System.Text.Json.Serialization;

namespace Kofre.Server.Accounts;

/// <summary>
/// A user of the server, as kept in <c>accounts.json</c>. The admin approves it by setting
/// <see cref="Approved"/>, and can disable it (an account waiting for approval: rejects it).
/// </summary>
public sealed record Account(Guid Id, string Email, string PasswordHash, DateTimeOffset Created, bool Approved = false, bool Disabled = false)
{
    /// <summary>Whether it may sign in and use the API. Computed, never written: only <see cref="Approved"/> and <see cref="Disabled"/> are the admin's.</summary>
    [JsonIgnore]
    public bool IsActive => Approved && !Disabled;

    /// <summary>Whether it waits for the admin's approval. Computed, never written.</summary>
    [JsonIgnore]
    public bool IsPending => !Approved && !Disabled;
}

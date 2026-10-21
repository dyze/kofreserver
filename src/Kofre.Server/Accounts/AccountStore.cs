using Kofre.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Accounts;

/// <summary>
/// The accounts, in <c>accounts.json</c>. The file is read again at every call, so the admin's
/// edits (approving, disabling) apply at once, without a restart.
/// </summary>
internal sealed class AccountStore(IOptions<KofreOptions> options)
{
    public const string FileName = "accounts.json";

    private readonly Lock _lock = new();

    private string FilePath => Path.Combine(options.Value.DataFolder, FileName);

    public Account? Find(Guid id) => ReadAll().FirstOrDefault(account => account.Id == id);

    public Account? FindByEmail(string email) => ReadAll().FirstOrDefault(account => SameEmail(account.Email, email));

    /// <summary>
    /// Adds an account waiting for approval, unless <see cref="KofreOptions.MaxPendingAccounts"/>
    /// wait already or the email is taken. Counted and added at once, so sign-ups at the same
    /// moment cannot pass the limit together.
    /// </summary>
    public SignUp Add(string email, string passwordHash, DateTimeOffset now)
    {
        lock (_lock)
        {
            var accounts = ReadAll();
            if (accounts.Count(account => account.IsPending) >= options.Value.MaxPendingAccounts)
            {
                return new SignUp(null, SignUpRefusal.TooManyPending);
            }

            if (accounts.Any(account => SameEmail(account.Email, email)))
            {
                return new SignUp(null, SignUpRefusal.EmailTaken);
            }

            var added = new Account(Guid.NewGuid(), email, passwordHash, now);
            JsonFile.Write(FilePath, accounts.Append(added).ToList());
            return new SignUp(added, null);
        }
    }

    public void SetPasswordHash(Guid id, string passwordHash)
    {
        lock (_lock)
        {
            var accounts = ReadAll().Select(account => account.Id == id ? account with { PasswordHash = passwordHash } : account);
            JsonFile.Write(FilePath, accounts.ToList());
        }
    }

    private List<Account> ReadAll()
    {
        lock (_lock)
        {
            return JsonFile.Read<List<Account>>(FilePath, []);
        }
    }

    private static bool SameEmail(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Why a sign-up was refused.</summary>
public enum SignUpRefusal
{
    EmailTaken,

    /// <summary>As many accounts as allowed wait for approval already (none when sign-ups are closed).</summary>
    TooManyPending,
}

/// <summary>The account added, or why none was.</summary>
public sealed record SignUp(Account? Added, SignUpRefusal? Refused);

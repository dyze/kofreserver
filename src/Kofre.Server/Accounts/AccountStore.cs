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

    /// <summary>Adds an account waiting for approval; null when the email is taken.</summary>
    public Account? Add(string email, string passwordHash, DateTimeOffset now)
    {
        lock (_lock)
        {
            var accounts = ReadAll();
            if (accounts.Any(account => SameEmail(account.Email, email)))
            {
                return null;
            }

            var added = new Account(Guid.NewGuid(), email, passwordHash, now);
            JsonFile.Write(FilePath, accounts.Append(added).ToList());
            return added;
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

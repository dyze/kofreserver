using Kofre.Server.Storage;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Accounts;

/// <summary>
/// An account's data, opaque to the server: a public part any signed-in account reads, a private
/// part only its account reads. <see cref="Version"/> counts its writes (0: none yet).
/// </summary>
public sealed record AccountData(long Version, byte[]? Public, byte[]? Private, DateTimeOffset? Updated = null);

/// <summary>
/// The accounts' data, one file each in <c>account-data/</c>. A write names the version it
/// replaces, so two devices cannot overwrite each other unknowingly.
/// </summary>
internal sealed class AccountDataStore(IOptions<KofreOptions> options)
{
    public const string FolderName = "account-data";

    private static readonly AccountData None = new(0, null, null);

    private readonly Lock _lock = new();

    public AccountData Read(Guid account)
    {
        lock (_lock)
        {
            return JsonFile.Read(PathOf(account), None);
        }
    }

    /// <summary>Replaces the data when its version is still <paramref name="replaces"/>; else leaves it. Returns the data now kept.</summary>
    public (AccountData Kept, bool Written) Write(Guid account, long replaces, byte[] publicPart, byte[] privatePart, DateTimeOffset now)
    {
        lock (_lock)
        {
            var current = Read(account);
            if (current.Version != replaces)
            {
                return (current, false);
            }

            var written = new AccountData(current.Version + 1, publicPart, privatePart, now);
            JsonFile.Write(PathOf(account), written);
            return (written, true);
        }
    }

    private string PathOf(Guid account) => Path.Combine(options.Value.DataFolder, FolderName, account.ToString("N") + ".json");
}

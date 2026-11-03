using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Kofre.Server.Email;
using Kofre.Server.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kofre.Server.Tests;

/// <summary>A server in memory, with its own data folder and a clock the test moves.</summary>
internal sealed class KofreServer(Dictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    public const string Password = "correct horse battery";

    /// <summary>Holds the data, logs and backups folders.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "kofre-tests-" + Guid.NewGuid().ToString("N"));

    public string DataFolder => Path.Combine(Root, "data");

    public string LogsFolder => Path.Combine(Root, "logs");

    public string BackupsFolder => Path.Combine(Root, "backups");

    public FakeMailSender Mail { get; } = new();

    public FakeDiskSpace Disk { get; } = new();

    public ManualClock Clock { get; } = new();

    public string AccountsFile => Path.Combine(DataFolder, AccountStore.FileName);

    public T Service<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>A client signed in as a new, approved account.</summary>
    public async Task<HttpClient> SignedIn(string email = "ada@example.com")
    {
        var client = CreateClient();
        (await client.PostAsJsonAsync("/v1/accounts", new Credentials(email, Password))).EnsureSuccessStatusCode();
        Edit(email, account => account with { Approved = true });
        var session = await SignIn(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    public static async Task<Session> SignIn(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/v1/sessions", new Credentials(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Session>())!;
    }

    /// <summary>Edits <c>accounts.json</c> as the admin does.</summary>
    public void Edit(string email, Func<Account, Account> change)
    {
        var accounts = JsonFile.Read<List<Account>>(AccountsFile, []);
        JsonFile.Write(AccountsFile, accounts.Select(account => account.Email == email ? change(account) : account).ToList());
    }

    public Account Account(string email) => JsonFile.Read<List<Account>>(AccountsFile, []).Single(account => account.Email == email);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Kofre:DataFolder", DataFolder);
        builder.UseSetting("Kofre:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("Kofre:PasswordIterations", "10");
        builder.UseSetting("Kofre:SignInsPerMinute", "1000");
        builder.UseSetting("Kofre:LookupsPerMinute", "1000");
        builder.UseSetting("Kofre:Logs:Folder", LogsFolder);
        builder.UseSetting("Kofre:Backup:Folder", BackupsFolder);
        builder.UseSetting("Kofre:Backup:Enabled", "false");
        builder.UseSetting("Kofre:Email:Host", "smtp.example.org");
        builder.UseSetting("Kofre:Email:From", "kofre@example.org");
        builder.UseSetting("Kofre:Email:To", "admin@example.org");
        builder.UseSetting("Kofre:Backup:ConfigFiles:0", Path.Combine(Root, "appsettings.json"));
        foreach (var (key, value) in settings ?? [])
        {
            builder.UseSetting("Kofre:" + key, value);
        }

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IMailSender>(Mail);
            services.AddSingleton<IDiskSpace>(Disk);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DeleteFolder(Root);
    }

    /// <summary>
    /// Deletes a test's folder, tried again a few times: on Windows, the antivirus or the search indexer
    /// opens the files just written for a moment. A folder still in use is left in the temporary folder.
    /// </summary>
    public static void DeleteFolder(string folder)
    {
        for (var attempt = 1; Directory.Exists(folder); attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                return;
            }
        }
    }
}

/// <summary>A clock the test moves by hand, starting now so tokens stay valid for the real clock too.</summary>
internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

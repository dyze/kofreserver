using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Kofre.Server.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Kofre.Server.Tests;

/// <summary>A server in memory, with its own data folder and a clock the test moves.</summary>
internal sealed class KofreServer(Dictionary<string, string>? settings = null) : WebApplicationFactory<Program>
{
    public const string Password = "correct horse battery";

    public string DataFolder { get; } = Path.Combine(Path.GetTempPath(), "kofre-tests-" + Guid.NewGuid().ToString("N"));

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
        foreach (var (key, value) in settings ?? [])
        {
            builder.UseSetting("Kofre:" + key, value);
        }

        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(DataFolder))
        {
            Directory.Delete(DataFolder, recursive: true);
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

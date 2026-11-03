using System.Net;
using System.Net.Http.Json;
using Kofre.Server.Backups;
using Kofre.Server.Email;
using Kofre.Server.Logging;
using Microsoft.Extensions.Logging;
using static Kofre.Server.Tests.StoreApiTests;

namespace Kofre.Server.Tests;

public sealed class EmailTests : IDisposable
{
    private readonly KofreServer _server = new(new() { ["Email:Enabled"] = "true" });

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task TheAdmin_IsToldWhenTheServerStartsAndStops()
    {
        var server = new KofreServer(new() { ["Email:Enabled"] = "true" });
        _ = server.Services;

        Assert.Contains(VersionEndpoint.Current, (await server.Mail.Next("started")).Body, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(server.DataFolder, ServiceLifecycle.MarkerFile)));

        server.Dispose();

        Assert.NotNull(await server.Mail.Next("stopping"));
    }

    [Fact]
    public async Task AStartAfterAnUncleanStop_IsReported()
    {
        using var server = new KofreServer(new() { ["Email:Enabled"] = "true" });
        Directory.CreateDirectory(server.DataFolder);
        File.WriteAllText(Path.Combine(server.DataFolder, ServiceLifecycle.MarkerFile), "");

        _ = server.Services;

        Assert.Contains("unclean", (await server.Mail.Next("started")).Subject, StringComparison.Ordinal);
        Assert.Contains(server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Warning), entry => entry.Message.Contains("unclean", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASignUp_TellsTheAdminAnAccountWaits()
    {
        await _server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials("bob@example.com", KofreServer.Password));

        Assert.Contains("bob@example.com", (await _server.Mail.Next("waiting for approval")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoEmail_WhenDisabled()
    {
        using var server = new KofreServer();

        await server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials("bob@example.com", KofreServer.Password));

        Assert.Equal(0, server.Mail.Waiting);
    }

    [Fact]
    public async Task AFailedEmail_IsLogged_TheRequestStillServed()
    {
        _server.Mail.Failure = new IOException("SMTP server down");

        var response = await _server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials("bob@example.com", KofreServer.Password));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(_server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Warning), entry => entry.Message.Contains("SMTP server down", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDailySummary_TellsAccountsStoresBackupAndProblems_SinceTheLastOne()
    {
        var ada = await _server.SignedIn();
        await Create(ada);
        await _server.CreateClient().PostAsJsonAsync("/v1/accounts", new Credentials("bob@example.com", KofreServer.Password));
        var summary = _server.Service<DailySummary>();
        _server.Clock.Advance(TimeSpan.FromMinutes(1));
        _server.Service<ILogger<EmailTests>>().LogWarning("Something to look at");

        await summary.RunAsync(CancellationToken.None);

        var first = (await _server.Mail.Next("daily summary")).Body;
        Assert.Contains("1 active, 1 waiting for approval", first, StringComparison.Ordinal);
        Assert.Contains("waiting: bob@example.com", first, StringComparison.Ordinal);
        Assert.Contains("Stores: 1.", first, StringComparison.Ordinal);
        Assert.Contains("Last backup: none", first, StringComparison.Ordinal);
        Assert.Contains("Something to look at", first, StringComparison.Ordinal);

        await _server.Service<Backup>().RunAsync(CancellationToken.None);
        _server.Clock.Advance(TimeSpan.FromDays(1));
        await summary.RunAsync(CancellationToken.None);

        var second = (await _server.Mail.Next("daily summary")).Body;
        Assert.Contains($"Last backup: {_server.Service<Backup>().Last!.Archive}", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Something to look at", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDailySummary_TellsAFailedBackup()
    {
        Directory.CreateDirectory(_server.Root);
        File.WriteAllText(_server.BackupsFolder, "a file where the folder should be");
        await _server.Service<Backup>().RunAsync(CancellationToken.None);

        await _server.Service<DailySummary>().RunAsync(CancellationToken.None);

        Assert.Contains("Last backup: FAILED", (await _server.Mail.Next("daily summary")).Body, StringComparison.Ordinal);
    }
}

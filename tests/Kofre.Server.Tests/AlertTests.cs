using System.Net.Http.Json;
using Kofre.Server.Email;
using Kofre.Server.Logging;
using Kofre.Server.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Tests;

/// <summary>The alerts to the admin, and the limits that keep them from flooding the mailbox.</summary>
public sealed class AlertTests
{
    [Theory]
    [InlineData("", "kofre@example.org", "admin@example.org")]
    [InlineData("smtp.example.org", "", "admin@example.org")]
    [InlineData("smtp.example.org", "kofre@example.org", "admin")]
    public void IncompleteEmailSettings_StopTheStart(string host, string from, string to)
    {
        using var server = new KofreServer(new() { ["Email:Enabled"] = "true", ["Email:Host"] = host, ["Email:From"] = from, ["Email:To"] = to });

        var failure = Record.Exception(() => server.Services);

        Assert.Contains(EmailOptions.Rule, failure!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmailsPastTheHourlyLimit_AreLogged_ThenCountedInTheNextOne()
    {
        using var server = new KofreServer(new() { ["Email:Enabled"] = "true", ["Email:MaxPerHour"] = "2" });
        var client = server.CreateClient();

        foreach (var email in new[] { "a@example.com", "b@example.com", "c@example.com" })
        {
            await client.PostAsJsonAsync("/v1/accounts", new Credentials(email, KofreServer.Password));
        }

        Assert.Equal(["started", "account waiting for approval"], server.Mail.Subjects.Select(subject => subject.Split(": ")[1]));
        Assert.Equal(2, server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Warning).Count(entry => entry.Message.Contains("MaxPerHour", StringComparison.Ordinal)));

        server.Clock.Advance(TimeSpan.FromHours(1));
        await client.PostAsJsonAsync("/v1/accounts", new Credentials("d@example.com", KofreServer.Password));

        var next = server.Mail.Sent[^1];
        Assert.Contains("d@example.com", next.Body, StringComparison.Ordinal);
        Assert.Contains("2 emails were not sent before this one", next.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHourlyLimit_SurvivesARestart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "kofre-quota-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new KofreOptions { DataFolder = folder, Email = { MaxPerHour = 1 } });
        var clock = new ManualClock();
        try
        {
            Assert.Equal(0, new MailQuota(options, clock).TryTake());

            var restarted = new MailQuota(options, clock);
            Assert.Null(restarted.TryTake());
            Assert.Null(restarted.TryTake());

            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(2, restarted.TryTake());
        }
        finally
        {
            KofreServer.DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task LowDiskSpace_IsToldOnce_ThenWhenItIsBack()
    {
        using var server = new KofreServer(new() { ["Email:Enabled"] = "true" });
        var watch = server.Service<DiskWatch>();

        server.Disk.Usage = new(19, 100);
        watch.Check();
        watch.Check();

        Assert.Contains("data: 19 B free of 100 B (19 %)", (await server.Mail.Next("disk space low")).Body, StringComparison.Ordinal);
        Assert.Equal(1, server.Mail.Count("disk space low"));

        server.Disk.Usage = new(20, 100);
        watch.Check();

        Assert.NotNull(await server.Mail.Next("disk space back"));
    }

    [Fact]
    public void ADiskThatCannotBeMeasured_IsLogged()
    {
        using var server = new KofreServer();
        server.Disk.Failure = new IOException("no such device");

        server.Service<DiskWatch>().Check();

        Assert.Contains(server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Warning), entry => entry.Message.Contains("no such device", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDailySummary_TellsTheDisks()
    {
        using var server = new KofreServer(new() { ["Email:Enabled"] = "true" });
        _ = server.Services;
        server.Disk.Usage = new(10, 100);

        await server.Service<DailySummary>().RunAsync(CancellationToken.None);
        server.Disk.Failure = new IOException("no such device");
        await server.Service<DailySummary>().RunAsync(CancellationToken.None);

        var first = (await server.Mail.Next("daily summary")).Body;
        Assert.Contains("backups: 10 B free of 100 B (10 %), LOW", first, StringComparison.Ordinal);
        Assert.Contains("The data folder holds", first, StringComparison.Ordinal);
        Assert.Contains("unknown: no such device", (await server.Mail.Next("daily summary")).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManyRefusedSignIns_AreToldOnce_UntilTheyFallBelowTheLimit()
    {
        using var server = new KofreServer(new() { ["Email:Enabled"] = "true", ["Email:MaxRefusedSignInsPerHour"] = "3" });
        var client = server.CreateClient();

        await RefuseSignIns(client, 4);
        Assert.Equal(1, server.Mail.Count("many sign-ins refused"));

        server.Clock.Advance(TimeSpan.FromHours(2));
        await RefuseSignIns(client, 3);
        Assert.Equal(2, server.Mail.Count("many sign-ins refused"));
    }

    [Fact]
    public void TheRealDisk_IsMeasured_EvenForAFolderNotMadeYet()
    {
        var usage = new DriveSpace().Of(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "not-made"));

        Assert.InRange(usage.Free, 0, usage.Total);
        Assert.True(usage.Total > 0);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.0 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 * 1024 * 1024, "3072.0 TB")]
    public void Sizes_AreReadable(long bytes, string text) => Assert.Equal(text, ByteSize.Format(bytes));

    [Fact]
    public void AnEmptyDisk_IsAllFree() => Assert.Equal(100, new DiskUsage(0, 0).FreePercent);

    private static async Task RefuseSignIns(HttpClient client, int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            await client.PostAsJsonAsync("/v1/sessions", new Credentials("nobody@example.com", "wrong password"));
        }
    }
}

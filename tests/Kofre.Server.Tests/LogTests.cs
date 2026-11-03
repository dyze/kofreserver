using Kofre.Server.Logging;
using Kofre.Server.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kofre.Server.Tests;

public sealed class LogTests
{
    [Fact]
    public void TheLog_IsWrittenToItsFolder()
    {
        using var server = new KofreServer();

        server.Service<ILogger<LogTests>>().LogInformation("Something happened");

        Assert.Contains("INF Kofre.Server.Tests.LogTests: Something happened", File.ReadAllText(Path.Combine(server.LogsFolder, FileLog.NameOf(0))), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheConsoleTheFilesAndTheMemory_Log()
    {
        using var server = new KofreServer();

        Assert.Equal(["ConsoleLoggerProvider", "FileLog", "MemoryLog"], server.Services.GetServices<ILoggerProvider>().Select(provider => provider.GetType().Name).Order());
    }

    [Fact]
    public void AspNetCore_LogsItsWarnings_ItsDataProtectionItsErrors()
    {
        using var server = new KofreServer();
        var loggers = server.Service<ILoggerFactory>();

        loggers.CreateLogger("Microsoft.AspNetCore.Routing").LogInformation("Routing information");
        loggers.CreateLogger("Microsoft.AspNetCore.Routing").LogWarning("Routing warning");
        loggers.CreateLogger("Microsoft.AspNetCore.DataProtection.KeyManagement").LogWarning("Keys in memory");
        loggers.CreateLogger("Microsoft.AspNetCore.DataProtection.KeyManagement").LogError("Keys error");

        Assert.Equal(["Routing warning", "Keys error"], server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Trace)
            .Where(entry => entry.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)).Select(entry => entry.Message));
    }

    [Fact]
    public void AFullFile_IsRotated_TheOldestDeleted()
    {
        using var server = new KofreServer(new() { ["Logs:MaxFileBytes"] = "200", ["Logs:MaxFiles"] = "3" });
        var log = server.Service<ILogger<LogTests>>();

        for (var line = 0; line < 50; line++)
        {
            log.LogInformation("Line {Line}", line);
        }

        Assert.Equal(["kofre.1.log", "kofre.2.log", "kofre.log"], Directory.EnumerateFiles(server.LogsFolder).Select(file => Path.GetFileName(file)).Order());
        Assert.Contains("Line 49", File.ReadAllText(Path.Combine(server.LogsFolder, FileLog.NameOf(0))), StringComparison.Ordinal);
    }

    [Fact]
    public void NoFolder_NoFile()
    {
        using var server = new KofreServer(new() { ["Logs:Folder"] = "" });

        server.Service<ILogger<LogTests>>().LogInformation("Something happened");

        Assert.False(Directory.Exists(server.LogsFolder));
    }

    [Fact]
    public void AFolderThatCannotBeWritten_LosesTheLines_NotTheServer()
    {
        var blocked = Path.Combine(Path.GetTempPath(), "kofre-blocked-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocked, "a file where the folder should be");
        try
        {
            using var server = new KofreServer(new() { ["Logs:Folder"] = blocked });

            server.Service<ILogger<LogTests>>().LogWarning("Lost");

            Assert.Contains(server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Warning), entry => entry.Message == "Lost");
        }
        finally
        {
            File.Delete(blocked);
        }
    }

    [Fact]
    public void TheMemory_KeepsTheLatestEntries()
    {
        using var server = new KofreServer(new() { ["Logs:MemoryEntries"] = "5" });
        var log = server.Service<ILogger<LogTests>>();

        for (var line = 0; line < 10; line++)
        {
            log.LogWarning("Line {Line}", line);
        }

        var kept = server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Trace);
        Assert.Equal(5, kept.Count);
        Assert.Equal("Line 9", kept[^1].Message);
    }

    [Fact]
    public void AnException_IsLoggedWithItsMessage()
    {
        using var server = new KofreServer();
        var log = server.Service<ILogger<LogTests>>();

        using (log.BeginScope("ignored"))
        {
            log.LogError(new IOException("disk full"), "Write failed");
        }

        var entry = server.Service<MemoryLog>().Since(DateTimeOffset.MinValue, LogLevel.Error).Single();
        Assert.Contains("disk full", entry.Message, StringComparison.Ordinal);
        Assert.False(log.IsEnabled(LogLevel.None));
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void AnEntry_ShowsItsLevel(LogLevel level, string name) =>
        Assert.Equal($"2026-11-03T10:20:30.000Z {name} Category: Message", new LogEntry(new DateTimeOffset(2026, 11, 3, 10, 20, 30, TimeSpan.Zero), level, "Category", "Message").ToString());

    [Theory]
    [InlineData("2026-11-03T02:00:00Z", 1)]
    [InlineData("2026-11-03T03:00:00Z", 24)]
    [InlineData("2026-11-03T04:00:00Z", 23)]
    public void ADailyTask_WaitsForItsTime(string now, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), DailyService.Until(DateTimeOffset.Parse(now), TimeSpan.FromHours(3), TimeZoneInfo.Utc));

    [Fact]
    public void ADailyTask_RunsAtTheLocalTime()
    {
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("plus-two", TimeSpan.FromHours(2), "plus-two", "plus-two");

        Assert.Equal(TimeSpan.FromMinutes(30), DailyService.Until(DateTimeOffset.Parse("2026-11-03T00:30:00Z"), TimeSpan.FromHours(3), plusTwo));
    }
}

using Kofre.Server.Api;
using Microsoft.Extensions.Options;

namespace Kofre.Server.Email;

/// <summary>
/// Tells the admin when the server starts and stops. A marker file lives while it runs: found at
/// start, the last stop was unclean (crash, power loss), with no stop email.
/// </summary>
internal sealed class ServiceLifecycle(IOptions<KofreOptions> options, AdminMail mail, TimeProvider clock, ILogger<ServiceLifecycle> log) : IHostedService
{
    public const string MarkerFile = "server.running";

    private string Marker => Path.Combine(options.Value.DataFolder, MarkerFile);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var unclean = File.Exists(Marker);
        Directory.CreateDirectory(options.Value.DataFolder);
        File.WriteAllText(Marker, $"{clock.GetUtcNow():O}");
        if (unclean)
        {
            log.LogWarning("Server {Version} started after an unclean stop", VersionEndpoint.Current);
        }
        else
        {
            log.LogInformation("Server {Version} started", VersionEndpoint.Current);
        }

        mail.Post(unclean ? "started after an unclean stop" : "started", Describe(unclean ? "started; the last stop was unclean (crash or power loss)" : "started"));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("Server {Version} stopping", VersionEndpoint.Current);
        await mail.SendAsync("stopping", Describe("stopping"), cancellationToken);
        File.Delete(Marker);
    }

    private string Describe(string what) => $"The Kofre server {VersionEndpoint.Current} on {Environment.MachineName} {what} at {clock.GetUtcNow():u}.";
}

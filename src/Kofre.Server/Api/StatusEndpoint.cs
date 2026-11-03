using Kofre.Server.Maintenance;

namespace Kofre.Server.Api;

public sealed record ServerStatus(bool Maintenance);

/// <summary>The server's state, without signing in: a client waits while maintenance is on.</summary>
internal static class StatusEndpoint
{
    public static void Map(RouteGroupBuilder api) =>
        api.MapGet("/status", (MaintenanceMode maintenance) => Results.Ok(new ServerStatus(maintenance.IsOn)));
}

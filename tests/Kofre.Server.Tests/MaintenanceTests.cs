using System.Net;
using System.Net.Http.Json;
using Kofre.Server.Maintenance;
using static Kofre.Server.Tests.AccountTests;
using static Kofre.Server.Tests.StoreApiTests;

namespace Kofre.Server.Tests;

public sealed class MaintenanceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly KofreServer _server = new();

    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task WhileMaintenanceIsOn_WritesAreRefused_ReadsWork()
    {
        var ada = await _server.SignedIn();
        var id = await Create(ada);
        var maintenance = _server.Service<MaintenanceMode>();

        Assert.True(await maintenance.TurnOnAsync(Timeout, TimeProvider.System, CancellationToken.None));

        Assert.True((await ada.GetFromJsonAsync<ServerStatus>("/v1/status"))!.Maintenance);
        var refused = await ada.PutAsync($"/v1/stores/{id}/files/a", Bytes("x"));
        await AssertRefused(refused, HttpStatusCode.ServiceUnavailable, "maintenance");
        Assert.Equal(TimeSpan.FromSeconds(MaintenanceGate.RetryAfterSeconds), refused.Headers.RetryAfter!.Delta);
        Assert.Equal(HttpStatusCode.OK, (await ada.GetAsync($"/v1/stores/{id}/feed")).StatusCode);

        maintenance.TurnOff();

        Assert.False((await _server.CreateClient().GetFromJsonAsync<ServerStatus>("/v1/status"))!.Maintenance);
        Assert.Equal(HttpStatusCode.Created, (await ada.PutAsync($"/v1/stores/{id}/files/a", Bytes("x"))).StatusCode);
    }

    [Fact]
    public async Task TurningOn_WaitsForTheRunningWrites()
    {
        var maintenance = new MaintenanceMode();
        Assert.True(maintenance.TryStartWrite());

        var turningOn = maintenance.TurnOnAsync(Timeout, TimeProvider.System, CancellationToken.None);

        Assert.False(turningOn.IsCompleted);
        Assert.False(maintenance.TryStartWrite());
        maintenance.EndWrite();
        Assert.True(await turningOn);
        Assert.True(maintenance.IsOn);
    }

    [Fact]
    public async Task WritesOutlastingTheTimeout_TurnItOffAgain()
    {
        var maintenance = new MaintenanceMode();
        Assert.True(maintenance.TryStartWrite());

        Assert.False(await maintenance.TurnOnAsync(TimeSpan.FromMilliseconds(20), TimeProvider.System, CancellationToken.None));

        Assert.False(maintenance.IsOn);
        Assert.True(maintenance.TryStartWrite());
    }
}

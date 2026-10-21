using System.Reflection;

namespace Kofre.Server.Api;

public sealed record ServerVersion(string Version);

/// <summary>The server's version, so a client can tell it is too old for what it needs.</summary>
internal static class VersionEndpoint
{
    /// <summary>"0.2": the version stamped into the assembly, without the commit added after "+".</summary>
    public static readonly string Current =
        typeof(VersionEndpoint).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    public static void Map(RouteGroupBuilder api) => api.MapGet("/version", () => Results.Ok(new ServerVersion(Current)));
}

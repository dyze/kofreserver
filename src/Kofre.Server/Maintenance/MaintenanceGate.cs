using Kofre.Server.Api;

namespace Kofre.Server.Maintenance;

/// <summary>Refuses the API's writes while maintenance is on (503 <c>maintenance</c>); reads still work.</summary>
internal static class MaintenanceGate
{
    public const int RetryAfterSeconds = 60;

    public static async Task Handle(HttpContext context, RequestDelegate next)
    {
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        {
            await next(context);
            return;
        }

        var maintenance = context.RequestServices.GetRequiredService<MaintenanceMode>();
        if (!maintenance.TryStartWrite())
        {
            context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();
            await Errors.Unavailable("maintenance").ExecuteAsync(context);
            return;
        }

        try
        {
            await next(context);
        }
        finally
        {
            maintenance.EndWrite();
        }
    }
}

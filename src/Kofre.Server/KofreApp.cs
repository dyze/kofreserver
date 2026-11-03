using System.Threading.RateLimiting;
using Kofre.Server.Accounts;
using Kofre.Server.Api;
using Kofre.Server.Backups;
using Kofre.Server.Email;
using Kofre.Server.Logging;
using Kofre.Server.Maintenance;
using Kofre.Server.Storage;
using Kofre.Server.Stores;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kofre.Server;

/// <summary>Builds the server: services, authentication, rate limits and the <c>/v1</c> API.</summary>
public static class KofreApp
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var services = builder.Services;
        // The console only (journald under systemd), besides the files and memory below: no Windows event log, no debugger output.
        builder.Logging.ClearProviders().AddConsole()
            .AddFilter("Microsoft.AspNetCore", LogLevel.Warning)
            // No cookies nor forms: its keys, kept in memory as the service has no home folder, are never used.
            .AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);

        services.AddOptions<KofreOptions>()
            .Bind(builder.Configuration.GetSection(KofreOptions.Section))
            .Validate(options => options.HasValidSigningKey, KofreOptions.SigningKeyRule)
            .Validate(options => options.Email.IsValid, EmailOptions.Rule)
            .ValidateOnStart();
        services.AddOptions<LogOptions>().Bind(builder.Configuration.GetSection(LogOptions.Section));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<MemoryLog>();
        services.AddSingleton<ILoggerProvider>(provider => provider.GetRequiredService<MemoryLog>());
        services.AddSingleton<ILoggerProvider, FileLog>();
        services.AddSingleton<AccountStore>();
        services.AddSingleton<AccountDataStore>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<StoreRepository>();
        services.AddSingleton<MaintenanceMode>();
        services.AddSingleton<IMailSender, MailKitSender>();
        services.AddSingleton<MailQuota>();
        services.AddSingleton<AdminMail>();
        services.AddSingleton<RefusedSignIns>();
        services.AddSingleton<IDiskSpace, DriveSpace>();
        // Started first, stopped last: its emails frame the others' work.
        services.AddHostedService<ServiceLifecycle>();
        services.AddHostedService<TrashPurge>();
        services.AddSingleton<DiskWatch>();
        services.AddHostedService(provider => provider.GetRequiredService<DiskWatch>());
        services.AddSingleton<Backup>();
        services.AddHostedService(provider => provider.GetRequiredService<Backup>());
        services.AddSingleton<DailySummary>();
        services.AddHostedService(provider => provider.GetRequiredService<DailySummary>());
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(JsonFile.Options.Converters[0]));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<KofreOptions>, TimeProvider>((jwt, kofre, clock) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = TokenService.Issuer,
                    ValidAudience = TokenService.Issuer,
                    IssuerSigningKey = TokenService.SigningKey(kofre.Value),
                    // Checked against the server's clock (replaced in tests), not the machine's.
                    LifetimeValidator = (notBefore, expires, _, _) =>
                        clock.GetUtcNow().UtcDateTime is var now && now >= notBefore - ClockSkew && now < expires + ClockSkew,
                };
            });
        services.AddAuthorization();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(AccountEndpoints.SignInLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<KofreOptions>>().Value.SignInsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
            limiter.AddPolicy(AccountDataEndpoints.LookupLimit, context => RateLimitPartition.GetFixedWindowLimiter(
                AccountDataEndpoints.LookupPartition(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<KofreOptions>>().Value.LookupsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        // Behind nginx or Caddy (on this machine): the client's address and scheme come from them.
        services.Configure<ForwardedHeadersOptions>(forwarded => forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

        var app = builder.Build();
        app.UseForwardedHeaders();
        app.Use(MaintenanceGate.Handle);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        var api = app.MapGroup("/v1");
        VersionEndpoint.Map(api);
        StatusEndpoint.Map(api);
        AccountEndpoints.Map(api);
        AccountDataEndpoints.Map(api);
        StoreEndpoints.Map(api);
        MemberEndpoints.Map(api);
        return app;
    }
}

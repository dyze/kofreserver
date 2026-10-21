using System.Threading.RateLimiting;
using Kofre.Server.Accounts;
using Kofre.Server.Api;
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

        services.AddOptions<KofreOptions>()
            .Bind(builder.Configuration.GetSection(KofreOptions.Section))
            .Validate(options => options.HasValidSigningKey, KofreOptions.SigningKeyRule)
            .ValidateOnStart();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AccountStore>();
        services.AddSingleton<AccountDataStore>();
        services.AddSingleton<TokenService>();
        services.AddSingleton<StoreRepository>();
        services.AddHostedService<TrashPurge>();
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
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        var api = app.MapGroup("/v1");
        VersionEndpoint.Map(api);
        AccountEndpoints.Map(api);
        AccountDataEndpoints.Map(api);
        StoreEndpoints.Map(api);
        MemberEndpoints.Map(api);
        return app;
    }
}

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Sync.Common.Auth;
using Sync.Common.Telemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

// API gateway (ADR 0004): the single public entry point for the app, the admin dashboard and the harness.
// Routes to the identity service and the Sync Gateway (routes in appsettings.json, ReverseProxy section),
// and handles the edge concerns once: JWT check, rate limits, CORS, body size. No business logic.
// YARP forwards traceparent and adds X-Forwarded-For/Proto/Host. Stateless; scale by replicas.
var builder = WebApplication.CreateBuilder(args);
builder.AddSyncTelemetry("api-gateway")
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation());

var jwt = builder.Configuration.GetSection("Jwt");
var limits = builder.Configuration.GetSection("RateLimits");

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Defence in depth: reject missing, expired or forged tokens here. Services still validate them too, and
// role checks (e.g. admin) stay in the services. Keys come from the identity service's JWKS, cached.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MetadataAddress = jwt["MetadataAddress"] ?? "http://localhost:8085/.well-known/openid-configuration";
        o.RequireHttpsMetadata = jwt.GetValue("RequireHttpsMetadata", false);
        o.RefreshInterval = TimeSpan.FromSeconds(30);
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"] ?? "melanin-wound-cdss",
            // Device and admin-dashboard tokens; the Sync Gateway itself accepts only the device audience.
            ValidAudiences = jwt.GetSection("Audiences").Get<string[]>()
                ?? ["melanin-wound-cdss-devices", "melanin-wound-cdss-admin"],
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimNames.Subject,
            RoleClaimType = ClaimNames.Role,
        };
    });
builder.Services.AddAuthorization();

// Only the admin dashboard calls from a browser.
builder.Services.AddCors(o => o.AddPolicy("dashboard", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

// "auth": per client IP, on top of the 5-attempt lockout per account (§7.3).
// "api": per signed-in device (or IP when there is no token), so 100 simulated devices on one host still work.
// A rejected request gets 429 with Retry-After, which the device already honours (§6.2, §7.1).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };

    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => PerMinute(limits.GetValue("AuthPerMinutePerIp", 120))));

    o.AddPolicy("api", http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.Identity?.IsAuthenticated == true
            ? $"{http.User.FindFirst(ClaimNames.Subject)?.Value}/{http.User.FindFirst(ClaimNames.DeviceId)?.Value}"
            : http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => PerMinute(limits.GetValue("ApiPerMinutePerClient", 600))));
});

var app = builder.Build();

// Kestrel enforces the limit, but YARP would report the failed body read as 400. The device splits a batch
// on 413 (§7.1), so answer that up front whenever Content-Length already shows the body is too big.
var maxBody = app.Configuration.GetValue<long?>("Kestrel:Limits:MaxRequestBodySize");
app.Use((context, next) =>
{
    if (context.Request.ContentLength > maxBody)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return Task.CompletedTask;
    }
    return next(context);
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapReverseProxy();

app.Run();

static FixedWindowRateLimiterOptions PerMinute(int permits) => new()
{
    PermitLimit = permits,
    Window = TimeSpan.FromMinutes(1),
    QueueLimit = 0,
};

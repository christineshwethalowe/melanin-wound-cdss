using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Sync.Common.Auth;
using Sync.Common.Evaluation;
using Sync.Common.Kafka;
using Sync.Common.Persistence;
using Sync.Common.Recommendations;
using SyncGateway.Auth;
using SyncGateway.Endpoints;
using SyncGateway.Push;
using SyncGateway.Validation;
using Sync.Common.Telemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

// Sync Gateway (architecture §4): push/pull, patient alias, figure proxy. Stateless; scale by replicas.
// Tokens come from the identity service (ADR 0003); this service only validates them.
var builder = WebApplication.CreateBuilder(args);
builder.AddSyncTelemetry("sync-gateway")
    .WithTracing(t => t.AddAspNetCoreInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation());

var dataSource = NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss");

var jwt = builder.Configuration.GetSection("Jwt");

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<WoundEventValidator>();
builder.Services.AddSingleton(new AblationOptions(builder.Configuration.GetValue<bool>(AblationOptions.ConfigKey)));
builder.Services.AddSingleton<PushService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DeviceRevocationCheck>();
// The Recommendation Service, for the figures proxy (§10.4) and the REST baseline (§13.1, which calls it inside the
// request with the same 60 s the orchestrator allows per attempt, but no retries).
builder.Services.AddSingleton(RecommendationResponseValidator.FromOutputDirectory());
builder.Services.AddHttpClient(BaselineEndpoint.RecommendationClient, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["RecommendationService:BaseUrl"] ?? "http://localhost:5080");
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<IProducer<string, byte[]>>(_ => new ProducerBuilder<string, byte[]>(
    KafkaDefaults.Producer(builder.Configuration["Kafka:BootstrapServers"] ?? KafkaDefaults.DefaultBootstrapServers,
        deliveryTimeoutMs: 10_000)).Build());

builder.Services.AddRequestDecompression(); // devices send gzip batches (§6.2)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Public keys come from the identity service's JWKS, fetched once and cached; a token with an unknown
        // kid triggers a refetch (key rotation). No call to the identity service per request.
        o.MetadataAddress = jwt["MetadataAddress"] ?? "http://localhost:8085/.well-known/openid-configuration";
        o.RequireHttpsMetadata = jwt.GetValue("RequireHttpsMetadata", false);
        o.RefreshInterval = TimeSpan.FromSeconds(30); // retry soon if the identity service was not up yet
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt["Issuer"] ?? "melanin-wound-cdss",
            ValidAudience = jwt["Audience"] ?? "melanin-wound-cdss-devices",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimNames.Subject,
            RoleClaimType = ClaimNames.Role,
        };
        // A valid signature is not enough for a mobile token: its device must not have been revoked (§12).
        // Admin-dashboard tokens carry no device and skip the check.
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var device = context.Principal?.FindFirst(ClaimNames.DeviceId)?.Value;
                if (device is null) return;
                var check = context.HttpContext.RequestServices.GetRequiredService<DeviceRevocationCheck>();
                if (!await check.IsAllowedAsync(device, context.HttpContext.RequestAborted))
                    context.Fail("DEVICE_REVOKED");
            },
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Services.GetRequiredService<AblationOptions>().Enabled) app.Logger.LogWarning(AblationOptions.Warning);

if (!app.Configuration.GetValue<bool>("SkipSchemaCheck"))
    await SchemaVersionGuard.EnsureAsync(dataSource, ExpectedSchemaVersions.All);

app.UseRequestDecompression();
app.UseAuthentication();
app.UseAuthorization();

// The device probes this before pushing: reachability, not just connectivity (§6.2).
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

var v1 = app.MapGroup("/v1");
v1.MapPatientEndpoints();
v1.MapPushEndpoint();
v1.MapPullEndpoint();
v1.MapFiguresProxyEndpoint();
v1.MapBaselineEndpoint();

await app.RunAsync();
return 0;

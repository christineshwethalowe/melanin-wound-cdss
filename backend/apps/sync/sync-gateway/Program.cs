using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Sync.Common.Auth;
using Sync.Common.Kafka;
using Sync.Common.Persistence;
using SyncGateway.Endpoints;
using SyncGateway.Push;
using SyncGateway.Validation;

// Sync Gateway (architecture §4): push/pull, patient alias, figure proxy. Stateless; scale by replicas.
// Tokens come from the identity service (ADR 0003); this service only validates them.
var builder = WebApplication.CreateBuilder(args);

var dataSource = NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss");

var jwt = builder.Configuration.GetSection("Jwt");

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<WoundEventValidator>();
builder.Services.AddSingleton<PushService>();
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
    });
builder.Services.AddAuthorization();

var app = builder.Build();

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

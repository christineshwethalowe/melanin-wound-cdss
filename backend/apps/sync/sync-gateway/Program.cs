using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Sync.Common.Kafka;
using Sync.Common.Persistence;
using SyncGateway.Auth;
using SyncGateway.Endpoints;
using SyncGateway.Push;
using SyncGateway.Validation;

// Sync Gateway (architecture §4): push/pull, clinician auth, figure proxy. Stateless; scale by replicas.
var builder = WebApplication.CreateBuilder(args);

var dataSource = NpgsqlDataSource.Create(
    builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Username=cdss;Password=cdss;Database=cdss");

// `dotnet run -- create-clinician ...` registers a clinician and exits (local testing and demos).
if (args.FirstOrDefault() == "create-clinician")
    return await CreateClinicianCommand.RunAsync(args, dataSource);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters (set Jwt__SigningKey).");

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<AuthService>();
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
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = jwt.Key,
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
v1.MapAuthEndpoints();
v1.MapPushEndpoint();
v1.MapPullEndpoint();
v1.MapFiguresProxyEndpoint();
v1.MapBaselineEndpoint();

await app.RunAsync();
return 0;

using IdentityService.Auth;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Endpoints;

/// <summary>
/// Basic OpenID Connect metadata (ADR 0003): enough for any resource server to find the issuer and the
/// public signing keys and validate tokens locally. Login itself stays the §7.3 API, not an OIDC flow.
/// </summary>
public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/openid-configuration", (HttpRequest request, JwtOptions jwt) =>
        {
            // URLs follow the address the caller used, so the same document works inside Docker and behind
            // the API gateway (which sends X-Forwarded-* headers).
            var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
            return Results.Ok(new Dictionary<string, object>
            {
                ["issuer"] = jwt.Issuer,
                ["jwks_uri"] = $"{baseUrl}/.well-known/jwks.json",
                ["token_endpoint"] = $"{baseUrl}/v1/auth/login",
                ["grant_types_supported"] = new[] { "password", "refresh_token" },
                ["response_types_supported"] = new[] { "token" },
                ["subject_types_supported"] = new[] { "public" },
                ["id_token_signing_alg_values_supported"] = new[] { SecurityAlgorithms.RsaSha256 },
                ["claims_supported"] = new[] { "sub", "device_id", "facility_id", "role" },
            });
        }).AllowAnonymous();

        // Public keys only. Resource servers cache this and refetch when they see an unknown kid.
        app.MapGet("/.well-known/jwks.json", (SigningKey key, HttpResponse response) =>
        {
            response.Headers.CacheControl = "public, max-age=300";
            return Results.Ok(new { keys = new[] { key.ToJwk() } });
        }).AllowAnonymous();

        return app;
    }
}

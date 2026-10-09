using System.Security.Cryptography;
using System.Text.Json;
using IdentityService.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Tests;

public class JwtTokenServiceTests
{
    private readonly JwtOptions _options = new();

    private TokenValidationParameters ValidationWith(SecurityKey key) => new()
    {
        ValidIssuer = _options.Issuer,
        ValidAudience = _options.Audience,
        IssuerSigningKey = key,
        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
    };

    /// <summary>What a resource server does: read the JWKS as published and validate with it.</summary>
    private static JsonWebKey FromPublishedJwks(SigningKey key) =>
        new JsonWebKeySet(JsonSerializer.Serialize(new { keys = new[] { key.ToJwk() } })).Keys.Single();

    [Fact]
    public async Task Token_validates_with_the_published_jwks()
    {
        var key = new SigningKey("");
        var token = new JwtTokenService(_options, key).CreateAccessToken(Guid.NewGuid(), "dev-a41c", "fac-001", "nurse");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, ValidationWith(FromPublishedJwks(key)));

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("RS256", ((JsonWebToken)result.SecurityToken).Alg);
        Assert.Equal(key.PublicKey.KeyId, ((JsonWebToken)result.SecurityToken).Kid);
        Assert.Equal("fac-001", result.Claims["facility_id"]);
    }

    [Fact]
    public async Task Token_from_another_key_is_rejected()
    {
        var token = new JwtTokenService(_options, new SigningKey("")).CreateAccessToken(Guid.NewGuid(), "d", "f", "nurse");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, ValidationWith(FromPublishedJwks(new SigningKey(""))));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Jwks_contains_no_private_key_material()
    {
        var jwk = FromPublishedJwks(new SigningKey(""));
        Assert.False(jwk.HasPrivateKey);
        Assert.Null(jwk.D);
    }

    [Fact]
    public void Configured_pem_gives_a_stable_key_id()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        Assert.Equal(new SigningKey(pem).PublicKey.KeyId, new SigningKey(pem).PublicKey.KeyId);
    }
}

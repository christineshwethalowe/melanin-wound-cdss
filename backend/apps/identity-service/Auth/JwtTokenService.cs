using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sync.Common.Auth;

namespace IdentityService.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "melanin-wound-cdss";
    public string Audience { get; set; } = "melanin-wound-cdss-devices";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// RSA private key (PKCS#8 or PKCS#1 PEM) used to sign access tokens. Set Jwt__SigningKeyPem outside local dev.
    /// When empty, a new key is generated at start-up: fine for a laptop, but every restart invalidates
    /// access tokens (devices simply refresh) and replicas would not share a key.
    /// </summary>
    public string SigningKeyPem { get; set; } = "";
}

/// <summary>
/// The RS256 signing key (ADR 0003). Only the identity service holds the private half; resource servers
/// fetch the public half from /.well-known/jwks.json and validate tokens locally.
/// </summary>
public sealed class SigningKey
{
    public RsaSecurityKey PrivateKey { get; }
    public RsaSecurityKey PublicKey { get; }
    public bool IsEphemeral { get; }

    public SigningKey(string pem)
    {
        var rsa = RSA.Create(2048);
        IsEphemeral = string.IsNullOrWhiteSpace(pem);
        if (!IsEphemeral) rsa.ImportFromPem(pem);

        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false));
        var kid = Base64UrlEncoder.Encode(publicKey.ComputeJwkThumbprint());
        PublicKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = kid };
        PrivateKey = new RsaSecurityKey(rsa) { KeyId = kid };
    }

    /// <summary>The public key as a JWK, as published in the JWKS.</summary>
    public object ToJwk()
    {
        var p = PublicKey.Parameters;
        return new
        {
            kty = "RSA",
            use = "sig",
            alg = SecurityAlgorithms.RsaSha256,
            kid = PublicKey.KeyId,
            n = Base64UrlEncoder.Encode(p.Modulus),
            e = Base64UrlEncoder.Encode(p.Exponent),
        };
    }
}

/// <summary>Issues the short-lived access JWT and the opaque refresh token (architecture §7.3).</summary>
public sealed class JwtTokenService(JwtOptions options, SigningKey key)
{
    private readonly JsonWebTokenHandler _handler = new();

    public int AccessTokenSeconds => options.AccessTokenMinutes * 60;
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(options.RefreshTokenDays);

    public string CreateAccessToken(Guid clinicianId, string deviceId, string facilityId, string role) =>
        _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Expires = DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(key.PrivateKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimNames.Subject, clinicianId.ToString()),
                new Claim(ClaimNames.DeviceId, deviceId),
                new Claim(ClaimNames.FacilityId, facilityId),
                new Claim(ClaimNames.Role, role),
            ]),
        });

    /// <summary>Returns the raw token for the device and the SHA-256 hash that is the only thing stored.</summary>
    public static (string Raw, byte[] Hash) CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, HashRefreshToken(raw));
    }

    public static byte[] HashRefreshToken(string raw) => SHA256.HashData(Encoding.UTF8.GetBytes(raw));
}

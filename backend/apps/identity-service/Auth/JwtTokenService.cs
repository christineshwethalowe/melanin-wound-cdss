using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SyncGateway.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "melanin-wound-cdss";
    public string Audience { get; set; } = "melanin-wound-cdss-devices";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>At least 32 characters. Override with the Jwt__SigningKey environment variable outside local dev.</summary>
    public string SigningKey { get; set; } = "";

    public SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(SigningKey));
}

public static class ClaimNames
{
    public const string Subject = "sub";
    public const string DeviceId = "device_id";
    public const string FacilityId = "facility_id";
    public const string Role = "role";
}

/// <summary>Issues the short-lived access JWT and the opaque refresh token (architecture §7.3).</summary>
public sealed class JwtTokenService(JwtOptions options)
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
            SigningCredentials = new SigningCredentials(options.Key, SecurityAlgorithms.HmacSha256),
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

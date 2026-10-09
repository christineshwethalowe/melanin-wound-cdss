using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sync.Common.Auth;

namespace IdentityService.Auth;

/// <summary>
/// Who a session is issued to (ADR 0005). Mobile sessions are bound to a registered device; admin-dashboard
/// sessions run in a browser, have no device, and are for role admin only.
/// </summary>
public static class Clients
{
    public const string Mobile = "mobile";
    public const string AdminDashboard = "admin-dashboard";

    public static bool IsKnown(string client) => client is Mobile or AdminDashboard;
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "melanin-wound-cdss";
    public string Audience { get; set; } = "melanin-wound-cdss-devices";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// Audience of admin-dashboard tokens. The Sync Gateway accepts only <see cref="Audience"/>, so a dashboard
    /// token can never push, pull or touch patient records.
    /// </summary>
    public string DashboardAudience { get; set; } = "melanin-wound-cdss-admin";

    /// <summary>A browser session is shorter-lived than a device's.</summary>
    public int DashboardRefreshTokenHours { get; set; } = 12;

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
        if (!IsEphemeral)
        {
            try
            {
                rsa.ImportFromPem(NormalizePem(pem));
            }
            catch (Exception e) when (e is ArgumentException or CryptographicException)
            {
                // Never echo the value: it is (or is meant to be) a private key.
                throw new InvalidOperationException(
                    "Jwt:SigningKeyPem is not a usable RSA private key. Expected an unencrypted PKCS#8 or PKCS#1 PEM, " +
                    "given as the PEM text (newlines may be written as \\n), as base64 of the PEM file, or as a path to the file.",
                    e);
            }
        }

        var publicKey = new RsaSecurityKey(rsa.ExportParameters(false));
        var kid = Base64UrlEncoder.Encode(publicKey.ComputeJwkThumbprint());
        PublicKey = new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = kid };
        PrivateKey = new RsaSecurityKey(rsa) { KeyId = kid };
    }

    /// <summary>
    /// Undoes what env files and secret stores do to a multi-line PEM: literal <c>\n</c> escapes, surrounding
    /// quotes, newlines collapsed into spaces, base64 wrapping of the whole file, or a path given instead of contents.
    /// </summary>
    internal static string NormalizePem(string value)
    {
        var v = value.Trim().Trim('"', '\'').Trim();
        if (!v.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            if (File.Exists(v)) return NormalizePem(File.ReadAllText(v));
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(v, @"\s+", "")));
                if (decoded.Contains("-----BEGIN", StringComparison.Ordinal)) return NormalizePem(decoded);
            }
            catch (FormatException) { }
            return v;
        }

        v = v.Replace("\\r", "").Replace("\\n", "\n").Replace("\r", "");
        var match = Regex.Match(v, @"-----BEGIN ([A-Z0-9 ]+)-----(.*?)-----END \1-----", RegexOptions.Singleline);
        if (!match.Success) return v;

        // Rebuild with canonical 64-column lines so a PEM flattened onto one line still parses.
        var label = match.Groups[1].Value;
        var body = Regex.Replace(match.Groups[2].Value, @"\s+", "");
        var lines = Enumerable.Range(0, (body.Length + 63) / 64)
            .Select(i => body.Substring(i * 64, Math.Min(64, body.Length - i * 64)));
        return $"-----BEGIN {label}-----\n{string.Join('\n', lines)}\n-----END {label}-----\n";
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

    public TimeSpan RefreshTokenLifetime(string client) => client == Clients.AdminDashboard
        ? TimeSpan.FromHours(options.DashboardRefreshTokenHours)
        : TimeSpan.FromDays(options.RefreshTokenDays);

    /// <param name="deviceId">The device for a mobile session; null for the admin dashboard.</param>
    public string CreateAccessToken(Guid clinicianId, string? deviceId, string facilityId, string role,
        string client = Clients.Mobile)
    {
        var claims = new ClaimsIdentity(
        [
            new Claim(ClaimNames.Subject, clinicianId.ToString()),
            new Claim(ClaimNames.FacilityId, facilityId),
            new Claim(ClaimNames.Role, role),
            new Claim(ClaimNames.ClientId, client),
        ]);
        if (deviceId is not null) claims.AddClaim(new Claim(ClaimNames.DeviceId, deviceId));

        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = client == Clients.AdminDashboard ? options.DashboardAudience : options.Audience,
            Expires = DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(key.PrivateKey, SecurityAlgorithms.RsaSha256),
            Subject = claims,
        });
    }

    /// <summary>Returns the raw token for the device and the SHA-256 hash that is the only thing stored.</summary>
    public static (string Raw, byte[] Hash) CreateRefreshToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, HashRefreshToken(raw));
    }

    public static byte[] HashRefreshToken(string raw) => SHA256.HashData(Encoding.UTF8.GetBytes(raw));
}

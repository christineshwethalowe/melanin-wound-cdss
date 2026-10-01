using System.Collections.Concurrent;
using Npgsql;

namespace SyncGateway.Auth;

/// <summary>
/// Refuses access tokens of a revoked device (architecture §12). Tokens are validated offline against the JWKS, so
/// without this a revoked phone could keep pushing and pulling until its access token expired (15 minutes).
/// The answer per device is cached for <see cref="CacheFor"/>, so a revocation takes effect within that time while
/// the database sees at most one lookup per device per interval, not one per request.
/// </summary>
public sealed class DeviceRevocationCheck(NpgsqlDataSource db, TimeProvider clock)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (bool Allowed, DateTimeOffset CheckedAt)> _cache = new();

    /// <summary>False for a revoked device, and for one the identity service never registered.</summary>
    public async Task<bool> IsAllowedAsync(string deviceId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_cache.TryGetValue(deviceId, out var hit) && now - hit.CheckedAt < CacheFor) return hit.Allowed;

        await using var cmd = db.CreateCommand("SELECT revoked_at IS NULL FROM clinical.device WHERE device_id = @d");
        cmd.Parameters.AddWithValue("d", deviceId);
        var allowed = await cmd.ExecuteScalarAsync(ct) is true;
        _cache[deviceId] = (allowed, now);
        return allowed;
    }
}

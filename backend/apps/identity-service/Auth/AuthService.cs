using Npgsql;

namespace IdentityService.Auth;

public sealed record TokenPair(string AccessToken, string RefreshToken, int ExpiresIn);

public sealed record AuthResult(TokenPair? Tokens, string? ReasonCode)
{
    public static AuthResult Ok(TokenPair tokens) => new(tokens, null);
    public static AuthResult Fail(string reason) => new(null, reason);
}

/// <summary>
/// Login, refresh and logout against clinical.clinician / clinician_credential / clinician_session
/// (architecture §7.3, §9.1). Every attempt is written to audit.auth_audit.
/// </summary>
public sealed class AuthService(NpgsqlDataSource db, PasswordHasher hasher, JwtTokenService tokens, SecretProtector secrets)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <param name="totp">Required once the clinician has MFA enabled. A wrong code counts as a failed attempt.</param>
    public async Task<AuthResult> LoginAsync(string username, string password, string deviceId, string? totp, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        Guid clinicianId; string role, facilityId, passwordHash; byte[] salt; int failedAttempts; DateTime? lockedUntil;
        bool mfaEnabled; byte[]? mfaSecret; long? mfaLastStep;
        await using (var cmd = new NpgsqlCommand("""
            SELECT c.clinician_id, c.role, c.facility_id, cc.password_hash, cc.password_salt,
                   cc.failed_attempts, cc.locked_until, cc.mfa_enabled, cc.mfa_secret_encrypted, cc.mfa_last_used_step
            FROM clinical.clinician c
            JOIN clinical.clinician_credential cc USING (clinician_id)
            WHERE c.username = @u AND c.active
            FOR UPDATE OF cc
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("u", username);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
            {
                await r.DisposeAsync();
                await AuthAudit.WriteAsync(conn, tx, "LOGIN", username, null, deviceId, false, "INVALID_CREDENTIALS", ct);
                await tx.CommitAsync(ct);
                return AuthResult.Fail("INVALID_CREDENTIALS");
            }
            clinicianId = r.GetGuid(0); role = r.GetString(1); facilityId = r.GetString(2);
            passwordHash = r.GetString(3); salt = (byte[])r[4]; failedAttempts = r.GetInt32(5);
            lockedUntil = r.IsDBNull(6) ? null : r.GetDateTime(6);
            mfaEnabled = r.GetBoolean(7);
            mfaSecret = r.IsDBNull(8) ? null : (byte[])r[8];
            mfaLastStep = r.IsDBNull(9) ? null : r.GetInt64(9);
        }

        async Task<AuthResult> FailAttemptAsync(string reason)
        {
            var attempts = failedAttempts + 1;
            var lockNow = attempts >= MaxFailedAttempts;
            await Sql.ExecAsync(conn, tx, """
                UPDATE clinical.clinician_credential SET failed_attempts = @a, locked_until = @l WHERE clinician_id = @id
                """, ct, ("a", lockNow ? 0 : attempts), ("l", lockNow ? DateTime.UtcNow + LockoutDuration : null),
                ("id", clinicianId));
            await AuthAudit.WriteAsync(conn, tx, lockNow ? "LOCKOUT" : "LOGIN", username, clinicianId, deviceId, false,
                lockNow ? "CREDENTIAL_LOCKED" : reason, ct);
            await tx.CommitAsync(ct);
            return AuthResult.Fail(lockNow ? "CREDENTIAL_LOCKED" : reason);
        }

        if (lockedUntil > DateTime.UtcNow)
        {
            await AuthAudit.WriteAsync(conn, tx, "LOGIN", username, clinicianId, deviceId, false, "CREDENTIAL_LOCKED", ct);
            await tx.CommitAsync(ct);
            return AuthResult.Fail("CREDENTIAL_LOCKED");
        }

        if (!hasher.Verify(password, Convert.FromBase64String(passwordHash), salt))
            return await FailAttemptAsync("INVALID_CREDENTIALS");

        if (mfaEnabled && mfaSecret is not null)
        {
            // No code yet: the app should ask for one. Not a guess, so it does not count towards lockout.
            if (string.IsNullOrEmpty(totp))
            {
                await AuthAudit.WriteAsync(conn, tx, "LOGIN", username, clinicianId, deviceId, false, "MFA_REQUIRED", ct);
                await tx.CommitAsync(ct);
                return AuthResult.Fail("MFA_REQUIRED");
            }

            var step = Totp.Verify(secrets.Unprotect(mfaSecret, clinicianId), totp, DateTimeOffset.UtcNow, mfaLastStep);
            if (step is null) return await FailAttemptAsync("INVALID_TOTP");
            await Sql.ExecAsync(conn, tx,
                "UPDATE clinical.clinician_credential SET mfa_last_used_step = @s WHERE clinician_id = @id",
                ct, ("s", step.Value), ("id", clinicianId));
        }

        // Prototype rule: an unknown device is registered to the clinician's facility on first login.
        await Sql.ExecAsync(conn, tx, """
            INSERT INTO clinical.device (device_id, facility_id) VALUES (@d, @f) ON CONFLICT (device_id) DO NOTHING
            """, ct, ("d", deviceId), ("f", facilityId));
        await using (var check = new NpgsqlCommand(
            "SELECT facility_id = @f AND revoked_at IS NULL FROM clinical.device WHERE device_id = @d", conn, tx))
        {
            check.Parameters.AddWithValue("f", facilityId);
            check.Parameters.AddWithValue("d", deviceId);
            if (await check.ExecuteScalarAsync(ct) is not true)
            {
                await AuthAudit.WriteAsync(conn, tx, "LOGIN", username, clinicianId, deviceId, false, "DEVICE_NOT_ALLOWED", ct);
                await tx.CommitAsync(ct);
                return AuthResult.Fail("DEVICE_NOT_ALLOWED");
            }
        }

        await Sql.ExecAsync(conn, tx,
            "UPDATE clinical.clinician_credential SET failed_attempts = 0, locked_until = NULL WHERE clinician_id = @id",
            ct, ("id", clinicianId));
        var pair = await IssueSessionAsync(conn, tx, clinicianId, deviceId, facilityId, role, ct);
        await AuthAudit.WriteAsync(conn, tx, "LOGIN", username, clinicianId, deviceId, true, null, ct);
        await tx.CommitAsync(ct);
        return AuthResult.Ok(pair);
    }

    /// <summary>Rotates both tokens: the old session is revoked and a new one issued.</summary>
    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var session = await FindActiveSessionAsync(conn, tx, refreshToken, ct);
        if (session is null)
        {
            await AuthAudit.WriteAsync(conn, tx, "REFRESH", "(refresh)", null, null, false, "INVALID_REFRESH_TOKEN", ct);
            await tx.CommitAsync(ct);
            return AuthResult.Fail("INVALID_REFRESH_TOKEN");
        }

        var s = session.Value;
        await Sql.ExecAsync(conn, tx, "UPDATE clinical.clinician_session SET revoked_at = now() WHERE session_id = @s",
            ct, ("s", s.SessionId));
        var pair = await IssueSessionAsync(conn, tx, s.ClinicianId, s.DeviceId, s.FacilityId, s.Role, ct);
        await AuthAudit.WriteAsync(conn, tx, "REFRESH", s.Username, s.ClinicianId, s.DeviceId, true, null, ct);
        await tx.CommitAsync(ct);
        return AuthResult.Ok(pair);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var session = await FindActiveSessionAsync(conn, tx, refreshToken, ct);
        if (session is { } s)
        {
            await Sql.ExecAsync(conn, tx, "UPDATE clinical.clinician_session SET revoked_at = now() WHERE session_id = @s",
                ct, ("s", s.SessionId));
            await AuthAudit.WriteAsync(conn, tx, "LOGOUT", s.Username, s.ClinicianId, s.DeviceId, true, null, ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task<TokenPair> IssueSessionAsync(NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid clinicianId, string deviceId, string facilityId, string role, CancellationToken ct)
    {
        var (raw, hash) = JwtTokenService.CreateRefreshToken();
        await Sql.ExecAsync(conn, tx, """
            INSERT INTO clinical.clinician_session (clinician_id, device_id, refresh_token_hash, expires_at, last_seen_at)
            VALUES (@c, @d, @h, @e, now())
            """, ct, ("c", clinicianId), ("d", deviceId), ("h", hash), ("e", DateTime.UtcNow + tokens.RefreshTokenLifetime));

        return new TokenPair(tokens.CreateAccessToken(clinicianId, deviceId, facilityId, role), raw, tokens.AccessTokenSeconds);
    }

    private static async Task<(Guid SessionId, Guid ClinicianId, string DeviceId, string FacilityId, string Role, string Username)?>
        FindActiveSessionAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string refreshToken, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT s.session_id, c.clinician_id, s.device_id, c.facility_id, c.role, c.username
            FROM clinical.clinician_session s
            JOIN clinical.clinician c USING (clinician_id)
            WHERE s.refresh_token_hash = @h AND s.revoked_at IS NULL AND s.expires_at > now() AND c.active
            FOR UPDATE OF s
            """, conn, tx);
        cmd.Parameters.AddWithValue("h", JwtTokenService.HashRefreshToken(refreshToken));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return (r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5));
    }
}

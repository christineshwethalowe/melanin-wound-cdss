using System.Security.Claims;
using System.Text.RegularExpressions;
using Npgsql;
using SyncGateway.Auth;

namespace SyncGateway.Endpoints;

public sealed record PatientAliasRequest(string? DisplayAlias);

/// <summary>
/// The local patient record (architecture §9.1): lets signed-in clinicians see "whose wound is this" with a
/// label they chose (initials, bed number), without that label ever entering the sync path, Kafka or the
/// Recommendation Service. Everything is scoped to the clinician's facility.
/// </summary>
public static partial class PatientEndpoints
{
    public const int MaxAliasLength = 64;

    [GeneratedRegex("^p-[0-9a-f]{6,32}$")]
    private static partial Regex PatientRefPattern();

    public static RouteGroupBuilder MapPatientEndpoints(this RouteGroupBuilder group)
    {
        var patients = group.MapGroup("/patients").RequireAuthorization();

        patients.MapGet("/{patientRef}", async (string patientRef, ClaimsPrincipal user, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var cmd = db.CreateCommand("""
                SELECT p.patient_ref, p.display_alias, p.updated_at,
                       (SELECT count(*) FROM clinical.wound w WHERE w.patient_ref = p.patient_ref)
                FROM clinical.patient p
                WHERE p.patient_ref = @p AND p.facility_id = @f
                """);
            cmd.Parameters.AddWithValue("p", patientRef);
            cmd.Parameters.AddWithValue("f", user.FindFirstValue(ClaimNames.FacilityId)!);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return Results.NotFound(new { code = "NOT_FOUND" });
            return Results.Ok(new
            {
                patientRef = r.GetString(0),
                displayAlias = r.IsDBNull(1) ? null : r.GetString(1),
                updatedAt = r.GetDateTime(2),
                woundCount = r.GetInt64(3),
            });
        });

        // Creates the record if the device labels a patient before any assessment has synced.
        // A null or empty alias clears it.
        patients.MapPut("/{patientRef}/alias", async (string patientRef, PatientAliasRequest req, ClaimsPrincipal user,
            NpgsqlDataSource db, CancellationToken ct) =>
        {
            if (!PatientRefPattern().IsMatch(patientRef)) return Results.BadRequest(new { code = "INVALID_PATIENT_REF" });
            var alias = string.IsNullOrWhiteSpace(req.DisplayAlias) ? null : req.DisplayAlias.Trim();
            if (alias?.Length > MaxAliasLength) return Results.BadRequest(new { code = "ALIAS_TOO_LONG", max = MaxAliasLength });

            await using var cmd = db.CreateCommand("""
                INSERT INTO clinical.patient (patient_ref, facility_id, display_alias) VALUES (@p, @f, @a)
                ON CONFLICT (patient_ref) DO UPDATE SET display_alias = EXCLUDED.display_alias, updated_at = now()
                WHERE clinical.patient.facility_id = EXCLUDED.facility_id
                RETURNING patient_ref
                """);
            cmd.Parameters.AddWithValue("p", patientRef);
            cmd.Parameters.AddWithValue("f", user.FindFirstValue(ClaimNames.FacilityId)!);
            cmd.Parameters.AddWithValue("a", (object?)alias ?? DBNull.Value);

            // No row back means the patient belongs to another facility: report it as not found.
            return await cmd.ExecuteScalarAsync(ct) is null
                ? Results.NotFound(new { code = "NOT_FOUND" })
                : Results.Ok(new { patientRef, displayAlias = alias });
        });

        return group;
    }
}

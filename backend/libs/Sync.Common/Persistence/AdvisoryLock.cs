using Npgsql;

namespace Sync.Common.Persistence;

/// <summary>
/// Transaction-scoped lock on an assessment (architecture §9.3). Always take this before any
/// row lock on wound_assessment, patient or recommendation, never after, to avoid deadlocks.
/// Released automatically on commit or rollback.
/// </summary>
public static class AdvisoryLock
{
    public static async Task AcquireForAssessmentAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid assessmentId, CancellationToken ct)
    {
        await using var timeouts = new NpgsqlCommand(
            "SET LOCAL lock_timeout = '2s'; SET LOCAL statement_timeout = '5s';", connection, transaction);
        await timeouts.ExecuteNonQueryAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext(@id))", connection, transaction);
        cmd.Parameters.AddWithValue("id", assessmentId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

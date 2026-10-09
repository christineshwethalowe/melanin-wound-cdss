using Npgsql;

namespace SyncGateway.Auth;

/// <summary>
/// Registers a clinician from the command line (local testing and demos):
///   dotnet run --project backend/apps/sync/sync-gateway -- create-clinician &lt;username&gt; &lt;password&gt; &lt;role&gt; &lt;facilityId&gt; [full name]
/// Role is one of nurse, wound_specialist, admin. The facility must already exist.
/// </summary>
public static class CreateClinicianCommand
{
    public static async Task<int> RunAsync(string[] args, NpgsqlDataSource db)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("usage: create-clinician <username> <password> <role> <facilityId> [full name]");
            return 1;
        }

        var (username, password, role, facilityId) = (args[1], args[2], args[3], args[4]);
        var fullName = args.Length > 5 ? string.Join(' ', args[5..]) : username;
        var (hash, salt) = new PasswordHasher().Hash(password);

        await using var conn = await db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using var insert = new NpgsqlCommand("""
            WITH c AS (
                INSERT INTO clinical.clinician (username, full_name, role, facility_id)
                VALUES (@u, @n, @r, @f)
                RETURNING clinician_id
            )
            INSERT INTO clinical.clinician_credential (clinician_id, password_hash, password_salt)
            SELECT clinician_id, @h, @s FROM c
            RETURNING clinician_id
            """, conn, tx);
        insert.Parameters.AddWithValue("u", username);
        insert.Parameters.AddWithValue("n", fullName);
        insert.Parameters.AddWithValue("r", role);
        insert.Parameters.AddWithValue("f", facilityId);
        insert.Parameters.AddWithValue("h", Convert.ToBase64String(hash));
        insert.Parameters.AddWithValue("s", salt);

        var id = await insert.ExecuteScalarAsync();
        await tx.CommitAsync();
        Console.WriteLine($"Created clinician '{username}' ({role}, {facilityId}) with id {id}.");
        return 0;
    }
}

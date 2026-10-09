using Npgsql;
using IdentityService.Admin;

namespace IdentityService.Auth;

/// <summary>
/// Registers a clinician from the command line. Used to bootstrap the first admin of a facility and for
/// local testing; after that, admins register clinicians through POST /v1/admin/clinicians.
///   dotnet run --project backend/apps/identity-service -- create-clinician [--if-not-exists] &lt;username&gt; &lt;password&gt; &lt;role&gt; &lt;facilityId&gt; [full name]
/// Role is one of nurse, wound_specialist, admin. The facility must already exist.
/// With --if-not-exists an existing username is reported and skipped (its password is left unchanged) and the command
/// succeeds, so a seed step can run on every start; any other problem still fails it. Without it, a taken username is
/// an error, so a typo at the command line is not mistaken for a new account.
/// </summary>
public static class CreateClinicianCommand
{
    public static async Task<int> RunAsync(string[] args, NpgsqlDataSource db)
    {
        const string IfNotExists = "--if-not-exists";
        var skipExisting = args.Contains(IfNotExists);
        args = args.Where(a => a != IfNotExists).ToArray();
        if (args.Length < 5)
        {
            Console.Error.WriteLine("usage: create-clinician [--if-not-exists] <username> <password> <role> <facilityId> [full name]");
            return 1;
        }

        var (username, password, role, facilityId) = (args[1], args[2], args[3], args[4]);
        var fullName = args.Length > 5 ? string.Join(' ', args[5..]) : username;

        if (skipExisting && await ExistsAsync(db, username))
        {
            Console.WriteLine($"Clinician '{username}' already exists; skipped (password unchanged).");
            return 0;
        }

        var admin = new ClinicianAdminService(db, new PasswordHasher());
        var (id, error) = await admin.RegisterAsync(null, facilityId,
            new RegisterClinicianRequest(username, password, fullName, role), CancellationToken.None);

        if (error == "USERNAME_TAKEN" && skipExisting)
        {
            // Registered by someone else between the check and the insert.
            Console.WriteLine($"Clinician '{username}' already exists; skipped (password unchanged).");
            return 0;
        }
        if (error is not null)
        {
            Console.Error.WriteLine($"Could not create clinician '{username}': {error}");
            return 1;
        }
        Console.WriteLine($"Created clinician '{username}' ({role}, {facilityId}) with id {id}.");
        return 0;
    }

    /// <summary>Same normalisation as registration (trimmed, lower-case), so "N.Silva" finds n.silva.</summary>
    private static async Task<bool> ExistsAsync(NpgsqlDataSource db, string username)
    {
        await using var cmd = db.CreateCommand("SELECT EXISTS (SELECT 1 FROM clinical.clinician WHERE username = @u)");
        cmd.Parameters.AddWithValue("u", username.Trim().ToLowerInvariant());
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
}

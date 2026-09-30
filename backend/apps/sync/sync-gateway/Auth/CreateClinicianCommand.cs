using Npgsql;
using SyncGateway.Admin;

namespace SyncGateway.Auth;

/// <summary>
/// Registers a clinician from the command line. Used to bootstrap the first admin of a facility and for
/// local testing; after that, admins register clinicians through POST /v1/admin/clinicians.
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

        var admin = new ClinicianAdminService(db, new PasswordHasher());
        var (id, error) = await admin.RegisterAsync(null, facilityId,
            new RegisterClinicianRequest(username, password, fullName, role), CancellationToken.None);

        if (error is not null)
        {
            Console.Error.WriteLine($"Could not create clinician '{username}': {error}");
            return 1;
        }
        Console.WriteLine($"Created clinician '{username}' ({role}, {facilityId}) with id {id}.");
        return 0;
    }
}

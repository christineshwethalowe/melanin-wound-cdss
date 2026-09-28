namespace SyncGateway.Endpoints;

// Example endpoints only (architecture §7.3).
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth");

        // Issues a 15-minute JWT (sub, device_id, facility_id, role) and an opaque refresh token.
        // Five failed attempts lock the credential for 15 minutes and write audit.auth_audit.
        auth.MapPost("/login", () => Results.StatusCode(StatusCodes.Status501NotImplemented));

        // Rotates both tokens.
        auth.MapPost("/refresh", () => Results.StatusCode(StatusCodes.Status501NotImplemented));

        // Revokes the clinician_session row.
        auth.MapPost("/logout", () => Results.StatusCode(StatusCodes.Status501NotImplemented));

        return group;
    }
}

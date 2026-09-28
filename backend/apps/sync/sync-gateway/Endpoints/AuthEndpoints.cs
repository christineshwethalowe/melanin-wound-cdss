using SyncGateway.Auth;

namespace SyncGateway.Endpoints;

public sealed record LoginRequest(string Username, string Password, string DeviceId, string? Totp);
public sealed record RefreshRequest(string RefreshToken);

/// <summary>Clinician authentication (architecture §7.3). TOTP is accepted but not yet enforced (Should tier).</summary>
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth").AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest req, AuthService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password) ||
                string.IsNullOrWhiteSpace(req.DeviceId))
                return Results.BadRequest(new { code = "MISSING_FIELDS" });

            var result = await service.LoginAsync(req.Username, req.Password, req.DeviceId, ct);
            return ToHttp(result);
        });

        auth.MapPost("/refresh", async (RefreshRequest req, AuthService service, CancellationToken ct) =>
            ToHttp(await service.RefreshAsync(req.RefreshToken, ct)));

        auth.MapPost("/logout", async (RefreshRequest req, AuthService service, CancellationToken ct) =>
        {
            await service.LogoutAsync(req.RefreshToken, ct);
            return Results.NoContent();
        });

        return group;
    }

    private static IResult ToHttp(AuthResult result) => result.Tokens is { } t
        ? Results.Ok(new { accessToken = t.AccessToken, refreshToken = t.RefreshToken, expiresIn = t.ExpiresIn })
        : Results.Json(new { code = result.ReasonCode }, statusCode: StatusCodes.Status401Unauthorized);
}

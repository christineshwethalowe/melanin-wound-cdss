namespace SyncGateway.Endpoints;

// Endpoints not built yet. Push, pull and auth live in their own files.
public static class SyncEndpoints
{
    /// <summary>GET /v1/figures/{corpusVersion}/{figureId}: proxied so the device never calls the Recommendation Service (§10.4).</summary>
    public static RouteGroupBuilder MapFiguresProxyEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/figures/{corpusVersion}/{figureId}", (string corpusVersion, string figureId) =>
            Results.StatusCode(StatusCodes.Status501NotImplemented)).RequireAuthorization();
        return group;
    }

    /// <summary>
    /// POST /v1/baseline/assessments: the plain REST comparison for the evaluation (§13.1, plan phase 8).
    /// Writes PostgreSQL and calls the same Recommendation Service stub inside the request.
    /// </summary>
    public static RouteGroupBuilder MapBaselineEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/baseline/assessments", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        return group;
    }
}

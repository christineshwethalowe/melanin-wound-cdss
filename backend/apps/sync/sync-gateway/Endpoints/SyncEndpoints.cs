namespace SyncGateway.Endpoints;

// Example endpoints only. Routes and payloads follow architecture §7 and will change as the team agrees them.
public static class SyncEndpoints
{
    /// <summary>POST /v1/sync/push: validate, produce to wound-events with acks=all, return a result per event (§7.1).</summary>
    public static RouteGroupBuilder MapPushEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/sync/push", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        return group;
    }

    /// <summary>GET /v1/sync/changes?cursor=&amp;limit=: cursor-based pull, re-sending the last 60 s of changes (§7.2).</summary>
    public static RouteGroupBuilder MapPullEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/sync/changes", (long cursor, int? limit) =>
            Results.StatusCode(StatusCodes.Status501NotImplemented));
        return group;
    }

    /// <summary>GET /v1/figures/{corpusVersion}/{figureId}: proxied so the device never calls the Recommendation Service (§10.4).</summary>
    public static RouteGroupBuilder MapFiguresProxyEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/figures/{corpusVersion}/{figureId}", (string corpusVersion, string figureId) =>
            Results.StatusCode(StatusCodes.Status501NotImplemented));
        return group;
    }

    /// <summary>
    /// POST /v1/baseline/assessments: the plain REST comparison for the evaluation (§13.1).
    /// Writes PostgreSQL and calls the same Recommendation Service stub inside the request.
    /// </summary>
    public static RouteGroupBuilder MapBaselineEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/baseline/assessments", () => Results.StatusCode(StatusCodes.Status501NotImplemented));
        return group;
    }
}

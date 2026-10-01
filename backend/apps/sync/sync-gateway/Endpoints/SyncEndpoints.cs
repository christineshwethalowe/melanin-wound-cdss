namespace SyncGateway.Endpoints;

// Endpoints not built yet. Push, pull, patients and the REST baseline live in their own files.
public static class SyncEndpoints
{
    /// <summary>GET /v1/figures/{corpusVersion}/{figureId}: proxied so the device never calls the Recommendation Service (§10.4).</summary>
    public static RouteGroupBuilder MapFiguresProxyEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/figures/{corpusVersion}/{figureId}", (string corpusVersion, string figureId) =>
            Results.StatusCode(StatusCodes.Status501NotImplemented)).RequireAuthorization();
        return group;
    }
}

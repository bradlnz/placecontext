using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace PlaceContext.App;

public static class AppHealthEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health");
        endpoints.MapGet("/healthz", () => Results.Ok("ok"))
            .AllowAnonymous();
    }
}

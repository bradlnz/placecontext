using System.Globalization;
using System.Security.Claims;
using KiwiDefender;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using PlaceContext.Host.Tenancy;

namespace PlaceContext.Host.Security;

// Runs after ASP.NET authorization. Its "authenticated" scope never replaces endpoint permissions.
public sealed class KiwiDefenderMiddleware(RequestDelegate next, Defender defender,
    ILogger<KiwiDefenderMiddleware> logger)
{
    public async Task Invoke(HttpContext context, TenantHolder tenant)
    {
        var endpoint = context.GetEndpoint();
        var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var resource = action is not null ? $"{action.ControllerName}.{action.ActionName}"
            : context.Request.Path.StartsWithSegments("/mcp") ? "mcp" : null;
        if (resource is null || endpoint?.Metadata.GetMetadata<IAuthorizeData>() is null ||
            endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(context);
            return;
        }

        var target = tenant.Tenant?.Id.ToString() ?? "";
        var user = context.User;
        Principal? principal = null;
        if (user.Identity?.IsAuthenticated == true)
        {
            // Tokens/cookies were just validated by ASP.NET. Non-expiring keys get a request-local
            // freshness window; this does not create a credential or bypass subsequent validation.
            var expires = context.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?
                .Properties?.ExpiresUtc?.ToUnixTimeSeconds() ?? DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
            if (user.FindFirst("exp") is { } exp)
                expires = long.TryParse(exp.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                    ? Math.Min(expires, value) : 0;
            principal = new Principal(user.FindFirst("tenant")?.Value ?? target,
                user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? "",
                ["authenticated"], expires);
        }
        var decision = defender.Decide(new Input(principal, target, "http.request", context.Request.Method, resource));
        logger.LogInformation("KiwiDefender {Resource} {Action}: {Outcome} {Reason}, policy {Generation}, rule {Rule}",
            resource, context.Request.Method, decision.Outcome, decision.Reason, decision.Generation, decision.RuleId);
        // Decisions are logged above; release the SDK's bounded in-memory event queue.
        defender.DrainEvents();
        if (decision.Allowed)
        {
            await next(context);
            return;
        }
        context.Response.StatusCode = decision.Status;
        context.Response.Headers.CacheControl = "no-store";
        if (decision.RetryAfter > 0)
            context.Response.Headers.RetryAfter = decision.RetryAfter.ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new { error = "kiwidefender_denied", reason = decision.Reason },
            context.RequestAborted);
    }
}

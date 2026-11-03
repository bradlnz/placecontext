using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Controllers;
using PlaceContext.Application.Jobs;
using PlaceContext.Application.Tenancy;
using PlaceContext.Host.Controllers.Api;

namespace PlaceContext.Host.Auth;

public sealed class DataAccessAuditMiddleware(RequestDelegate next, ILogger<DataAccessAuditMiddleware> logger)
{
    public async Task Invoke(HttpContext context, ICurrentTenant tenant)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var controller = action?.ControllerTypeInfo.AsType();
        if (controller != typeof(ProjectDataController) && controller != typeof(JobDataController))
        {
            await next(context);
            return;
        }

        var jobAccess = controller == typeof(JobDataController);
        var scheme = jobAccess ? JobDataAuthenticationHandler.SchemeName : UserApiTokenAuthenticationHandler.SchemeName;
        var operation = (jobAccess ? "job-data." : "project-data.") + action!.ActionName;
        var tenantId = tenant.IsResolved ? tenant.TenantId : (Guid?)null;
        var started = Stopwatch.GetTimestamp();
        var status = StatusCodes.Status500InternalServerError;
        try
        {
            await next(context);
            status = context.RequestAborted.IsCancellationRequested ? 499 : context.Response.StatusCode;
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            status = 499;
            throw;
        }
        finally
        {
            // Read the endpoint's authenticated identity after authorization has selected its scheme.
            var identity = context.User.Identities.FirstOrDefault(value => value.IsAuthenticated && value.AuthenticationType == scheme);
            var actorId = Guid.TryParse(identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
            var grant = jobAccess && identity is not null ? context.Features.Get<JobDataGrant>() : null;
            var projectId = jobAccess ? grant?.ProjectId
                : Guid.TryParse(context.Request.RouteValues["projectId"]?.ToString(), out var project) ? project : (Guid?)null;
            var outcome = status is 401 or 403 ? "denied"
                : status == 499 ? "cancelled"
                : status >= 400 ? "failed" : "succeeded";
            // Only fixed labels and typed identifiers belong here; never log bodies, SQL, tokens or exceptions.
            logger.LogInformation(new EventId(7100, "DataAccess"),
                "Data access {AuditId}: trace {TraceId}, tenant {TenantId}, actor {ActorId}, scheme {AuthenticationScheme}, operation {Operation}, project {ProjectId}, job {JobId}, run {RunId}, outcome {Outcome}, status {StatusCode}, duration {DurationMs} ms",
                Guid.NewGuid(), Activity.Current?.TraceId.ToHexString(), tenantId, actorId, scheme, operation, projectId, grant?.JobId, grant?.RunId,
                outcome, status, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}

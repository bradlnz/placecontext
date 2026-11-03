using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using PlaceContext.Application.Jobs;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers.Api;
using PlaceContext.TestSupport;

namespace PlaceContext.Host.Tests;

public sealed class DataAccessAuditTests
{
    [Theory]
    [InlineData(200, "succeeded")]
    [InlineData(400, "failed")]
    [InlineData(401, "denied")]
    [InlineData(403, "denied")]
    [InlineData(429, "failed")]
    public async Task Personal_data_requests_audit_the_final_identity_and_outcome_without_payloads(int status, string outcome)
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var actor = Guid.NewGuid();
        var project = Guid.NewGuid();
        var context = Request(typeof(ProjectDataController));
        context.Request.RouteValues["projectId"] = project.ToString();
        var logger = new AuditLogger();
        var middleware = new DataAccessAuditMiddleware(http =>
        {
            http.User = Principal(actor, UserApiTokenAuthenticationHandler.SchemeName);
            http.Response.StatusCode = status;
            return Task.CompletedTask;
        }, logger);

        await middleware.Invoke(context, tenant);

        Assert.Equal(7100, logger.Event.Id);
        Assert.Equal(tenant.TenantId, logger.Values["TenantId"]);
        Assert.Equal(actor, logger.Values["ActorId"]);
        Assert.Equal(project, logger.Values["ProjectId"]);
        Assert.Equal("project-data.Query", logger.Values["Operation"]);
        Assert.Equal(outcome, logger.Values["Outcome"]);
        Assert.Equal(status, logger.Values["StatusCode"]);
        Assert.IsType<Guid>(logger.Values["AuditId"]);
        Assert.DoesNotContain("private-marker", JsonSerializer.Serialize(logger.Values));
        Assert.Null(logger.Exception);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Job_identity_and_scope_come_from_authentication_not_request_values(bool authenticated)
    {
        var tenant = new FakeCurrentTenant(Guid.NewGuid());
        var grant = new JobDataGrant(tenant.TenantId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5));
        var context = Request(typeof(JobDataController));
        context.Request.RouteValues["projectId"] = Guid.NewGuid().ToString();
        context.User = Principal(Guid.NewGuid(), "Cookies");
        var logger = new AuditLogger();
        var middleware = new DataAccessAuditMiddleware(http =>
        {
            if (authenticated)
            {
                http.User = Principal(grant.RunId, JobDataAuthenticationHandler.SchemeName);
                http.Features.Set(grant);
            }
            http.Response.StatusCode = authenticated ? 200 : 401;
            return Task.CompletedTask;
        }, logger);

        await middleware.Invoke(context, tenant);

        Assert.Equal(authenticated ? grant.RunId : (Guid?)null, logger.Values["ActorId"]);
        Assert.Equal(authenticated ? grant.ProjectId : (Guid?)null, logger.Values["ProjectId"]);
        Assert.Equal(authenticated ? grant.JobId : (Guid?)null, logger.Values["JobId"]);
        Assert.Equal(authenticated ? grant.RunId : (Guid?)null, logger.Values["RunId"]);
        Assert.Equal(authenticated ? "succeeded" : "denied", logger.Values["Outcome"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exceptions_and_disconnects_are_audited_without_exception_details(bool cancelled)
    {
        var context = Request(typeof(ProjectDataController));
        context.RequestAborted = new CancellationToken(cancelled);
        var logger = new AuditLogger();
        Exception failure = cancelled ? new OperationCanceledException("private-marker") : new InvalidOperationException("private-marker");
        var middleware = new DataAccessAuditMiddleware(_ => Task.FromException(failure), logger);

        var thrown = await Record.ExceptionAsync(() => middleware.Invoke(context, new FakeCurrentTenant(Guid.NewGuid())));

        Assert.Same(failure, thrown);
        Assert.Equal(cancelled ? 499 : 500, logger.Values["StatusCode"]);
        Assert.Equal(cancelled ? "cancelled" : "failed", logger.Values["Outcome"]);
        Assert.DoesNotContain("private-marker", JsonSerializer.Serialize(logger.Values));
        Assert.Null(logger.Exception);
    }

    [Fact]
    public async Task Disconnect_without_an_exception_is_not_reported_as_a_success()
    {
        var context = Request(typeof(ProjectDataController));
        context.RequestAborted = new CancellationToken(true);
        var logger = new AuditLogger();
        var middleware = new DataAccessAuditMiddleware(_ => Task.CompletedTask, logger);

        await middleware.Invoke(context, new FakeCurrentTenant(Guid.NewGuid()));

        Assert.Equal(499, logger.Values["StatusCode"]);
        Assert.Equal("cancelled", logger.Values["Outcome"]);
    }

    [Fact]
    public async Task Unrelated_requests_are_forwarded_without_data_audit_events()
    {
        var called = false;
        var logger = new AuditLogger();
        var middleware = new DataAccessAuditMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        }, logger);

        await middleware.Invoke(new DefaultHttpContext(), new FakeCurrentTenant(Guid.NewGuid()));

        Assert.True(called);
        Assert.Empty(logger.Values);
    }

    private static DefaultHttpContext Request(Type controller)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ControllerActionDescriptor
        {
            ControllerTypeInfo = controller.GetTypeInfo(), ActionName = "Query"
        }), "private-marker"));
        context.Request.Headers.Authorization = "Bearer private-marker";
        context.Request.QueryString = new QueryString("?sql=private-marker");
        context.Request.Body = new MemoryStream("private-marker"u8.ToArray());
        return context;
    }

    private static ClaimsPrincipal Principal(Guid id, string scheme) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Email, "private-marker@example.test")
    ], scheme));

    private sealed class AuditLogger : ILogger<DataAccessAuditMiddleware>
    {
        public Dictionary<string, object?> Values { get; private set; } = [];
        public EventId Event { get; private set; }
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Values = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(pair => pair.Key, pair => pair.Value);
            Event = eventId;
            Exception = exception;
        }
    }
}

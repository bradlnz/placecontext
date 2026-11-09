using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application;
using PlaceContext.Application.Access.Models;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;
using PlaceContext.Host.Api;
using PlaceContext.Host.Auth;
using PlaceContext.Host.Controllers.Api;

namespace PlaceContext.Host.Tests;

public sealed class ProjectDataApiTests
{
    [Fact]
    public async Task Query_requires_personal_token_data_read_and_dispatches_a_read_for_the_requested_project()
    {
        var authorization = Assert.Single(typeof(ProjectDataController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(UserApiTokenAuthenticationHandler.SchemeName, authorization.AuthenticationSchemes);
        Assert.Equal(Permission.DataRead, authorization.Policy);
        var dispatcher = new Queries();
        var controller = Create(dispatcher);
        var projectId = Guid.NewGuid();
        var response = Assert.IsType<OkObjectResult>(await controller.Query(projectId, new("SELECT count(*) FROM orders")));
        var data = Assert.IsType<ProjectDataQueryResponse>(response.Value);
        Assert.Equal("12", data.Rows[0][0]);
        Assert.Equal(projectId, dispatcher.Last!.ProjectId);
        Assert.Equal(Permission.DataRead, dispatcher.Last.RequiredPermission);
    }

    [Fact]
    public async Task Database_errors_do_not_expose_values_in_the_response()
    {
        var controller = Create(new Queries { Fail = true });
        var response = Assert.IsType<BadRequestObjectResult>(await controller.Query(Guid.NewGuid(), new("SELECT secret FROM customers")));
        Assert.DoesNotContain("person@example.com", System.Text.Json.JsonSerializer.Serialize(response.Value));
    }

    private static ProjectDataController Create(Queries dispatcher) => new(new PlaceContextService(dispatcher, null!, null!, null!))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private sealed class Queries : IDispatcher
    {
        public bool Fail;
        public QueryProjectDataQuery? Last;
        public Task<T> Query<T>(IQuery<T> query, CancellationToken ct = default)
        {
            Last = Assert.IsType<QueryProjectDataQuery>(query);
            if (Fail)
            {
                throw new DatabaseFailure();
            }
            return Task.FromResult((T)(object)new ProjectQueryResult(["count"], [["12"]], 0, false));
        }
        public Task<T> Send<T>(ICommand<T> command, CancellationToken ct = default) => throw new InvalidOperationException("Writes must not be dispatched.");
    }

    private sealed class DatabaseFailure() : DbException("Invalid source value person@example.com");
}

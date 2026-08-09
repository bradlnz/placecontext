using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Search.Contracts.Api;
using PlaceContext.Search.Controllers.Api;

namespace PlaceContext.Search.Tests;

public sealed class SearchApiControllerTests
{
    [Fact]
    public void Endpoint_keeps_the_v1_route_and_data_read_permission()
    {
        var route = Assert.Single(typeof(SearchController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>());
        Assert.Equal("api/v1/search", route.Template);

        var authorization = Assert.Single(typeof(SearchController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(Permission.DataRead, authorization.Policy);
    }

    [Fact]
    public void Search_registration_supplies_a_request_project_without_a_projects_reference()
    {
        var projectId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Project-Id"] = projectId.ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = context,
        });

        services.AddSearchApi();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var currentProject = scope.ServiceProvider.GetRequiredService<ICurrentProject>();
        Assert.True(currentProject.IsResolved);
        Assert.Equal(projectId, currentProject.ProjectId);
    }

    [Fact]
    public async Task Search_dispatches_a_bounded_project_query_and_filters_the_response()
    {
        var selected = Guid.NewGuid();
        var other = Guid.NewGuid();
        var dispatcher = new StubDispatcher
        {
            Result = new SearchResultsView(
                "customer",
                [
                    new SearchHit("artifact", selected, "First", "PDF", "/artifacts?artifact=1"),
                    new SearchHit("decision", other, "Other project", "hidden", "/project/other"),
                    new SearchHit("entity", selected, "Second", "CRM", "/project/selected/entity"),
                ]),
        };
        var controller = Controller(dispatcher, new StubCurrentProject(selected));

        var action = await controller.Search("  customer  ", limit: 1);

        var response = Assert.IsType<SearchApiResponse>(Assert.IsType<OkObjectResult>(action).Value);
        var query = Assert.IsType<SearchQuery>(dispatcher.LastQuery);
        Assert.Equal("customer", query.Term);
        Assert.Equal(100, query.Limit);
        Assert.Equal(selected, query.ProjectId);
        Assert.Equal(selected, response.ProjectId);
        Assert.Equal("customer", response.Query);
        Assert.Equal(1, response.Count);
        Assert.Equal("First", Assert.Single(response.Hits).Title);
    }

    [Theory]
    [InlineData(null, 25, "q must contain at least 2 characters.")]
    [InlineData(" x ", 25, "q must contain at least 2 characters.")]
    [InlineData("valid", 0, "limit must be between 1 and 100.")]
    [InlineData("valid", 101, "limit must be between 1 and 100.")]
    public async Task Search_rejects_invalid_query_values(
        string? query,
        int limit,
        string expectedError)
    {
        var dispatcher = new StubDispatcher();
        var controller = Controller(dispatcher, new StubCurrentProject(Guid.NewGuid()));

        var action = await controller.Search(query, limit);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Equal(expectedError, Error(badRequest.Value));
        Assert.Null(dispatcher.LastQuery);
    }

    [Fact]
    public async Task Search_rejects_queries_over_200_characters()
    {
        var dispatcher = new StubDispatcher();
        var controller = Controller(dispatcher, new StubCurrentProject(Guid.NewGuid()));

        var action = await controller.Search(new string('x', 201), 25);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Equal("q must be 200 characters or fewer.", Error(badRequest.Value));
        Assert.Null(dispatcher.LastQuery);
    }

    [Fact]
    public async Task Search_requires_a_resolved_project_before_dispatching()
    {
        var dispatcher = new StubDispatcher();
        var controller = Controller(dispatcher, new StubCurrentProject());

        var action = await controller.Search("valid", 25);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action);
        Assert.Equal(
            "No project resolved. Pass X-Project-Id (GUID) or X-Project (name) on the request.",
            Error(badRequest.Value));
        Assert.Null(dispatcher.LastQuery);
    }

    private static SearchController Controller(IDispatcher dispatcher, ICurrentProject project)
        => new(dispatcher, project)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    private static string Error(object? value)
        => (string)value!.GetType().GetProperty("error")!.GetValue(value)!;

    private sealed class StubCurrentProject(Guid? projectId = null) : ICurrentProject
    {
        public Guid ProjectId { get; } = projectId ?? Guid.Empty;
        public string ProjectName => "Test";
        public bool IsResolved => projectId.HasValue;
    }

    private sealed class StubDispatcher : IDispatcher
    {
        public SearchResultsView Result { get; init; } = new("unused", []);
        public object? LastQuery { get; private set; }

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult((TResult)(object)Result);
        }
    }
}

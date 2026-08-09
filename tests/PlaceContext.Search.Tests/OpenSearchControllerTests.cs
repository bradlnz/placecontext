using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Search.Controllers;

namespace PlaceContext.Search.Tests;

public sealed class OpenSearchControllerTests
{
    [Fact]
    public async Task Page_composes_the_selected_index_profile_and_dashboards()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new StubDispatcher
        {
            QueryResult = query => query switch
            {
                ListOpenSearchIndicesQuery => new[]
                {
                    new OpenSearchIndexView("first", 2, "1kb"),
                    new OpenSearchIndexView("second", 4, "2kb"),
                },
                ListOpenSearchDashboardsQuery => Array.Empty<OpenSearchDashboardView>(),
                ListOpenSearchFieldsQuery => new[]
                {
                    new OpenSearchFieldView("updated_at", "date", true, true),
                },
                GetOpenSearchLastUpdatedQuery => new OpenSearchLastUpdatedView(
                    DateTimeOffset.Parse("2026-08-09T01:00:00Z"),
                    "updated_at"),
                _ => throw new InvalidOperationException($"Unexpected query {query.GetType().Name}"),
            },
        };
        var controller = Controller(dispatcher);

        var result = await controller.Page(projectId, "second", default);

        var response = Assert.IsType<OpenSearchPageView>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("second", response.SelectedIndex);
        Assert.Single(response.Fields);
        Assert.Equal("updated_at", response.LastUpdated?.Field);
    }

    [Fact]
    public async Task Save_dashboard_uses_the_project_from_the_route()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new StubDispatcher
        {
            CommandResult = command =>
            {
                var save = Assert.IsType<SaveOpenSearchDashboardCommand>(command);
                return Dashboard(save.ProjectId, save.Name);
            },
        };
        var controller = Controller(dispatcher);

        var result = await controller.SaveDashboard(
            projectId,
            new SaveOpenSearchDashboardRequest(
                "Pipeline",
                "properties",
                null,
                "suburb",
                "terms",
                "bar",
                "count",
                null,
                null,
                "{}"),
            default);

        var response = Assert.IsType<OpenSearchDashboardView>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(projectId, response.ProjectId);
        Assert.Equal(projectId, Assert.IsType<SaveOpenSearchDashboardCommand>(dispatcher.LastCommand).ProjectId);
    }

    [Fact]
    public void Sync_requires_settings_permission()
    {
        var method = typeof(OpenSearchController).GetMethod(nameof(OpenSearchController.Sync));
        var attribute = Assert.Single(method!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(Permission.SettingsManage, attribute.Policy);
    }

    private static OpenSearchDashboardView Dashboard(Guid projectId, string name) => new(
        Guid.NewGuid(),
        projectId,
        name,
        "properties",
        null,
        "suburb",
        "terms",
        "bar",
        "count",
        null,
        null,
        "{}",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);

    private static OpenSearchController Controller(IDispatcher dispatcher) => new(
        dispatcher,
        new StubAuthorization())
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private sealed class StubAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            System.Security.Claims.ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Failed());

        public Task<AuthorizationResult> AuthorizeAsync(
            System.Security.Claims.ClaimsPrincipal user,
            object? resource,
            string policyName)
            => Task.FromResult(AuthorizationResult.Failed());
    }

    private sealed class StubDispatcher : IDispatcher
    {
        public Func<object, object?>? CommandResult { get; init; }
        public Func<object, object?>? QueryResult { get; init; }
        public object? LastCommand { get; private set; }

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            LastCommand = command;
            return Task.FromResult((TResult)CommandResult!(command)!);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => Task.FromResult((TResult)QueryResult!(query)!);
    }
}

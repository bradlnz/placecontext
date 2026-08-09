using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Projects.Api;
using PlaceContext.Projects.Auth;
using PlaceContext.Projects.Controllers;

namespace PlaceContext.Projects.Tests;

public sealed class ProjectPageControllerTests
{
    [Fact]
    public async Task Overview_context_dispatches_project_queries_and_maps_legacy_response()
    {
        var projectId = Guid.NewGuid();
        var decisionId = Guid.NewGuid();
        var changeId = Guid.NewGuid();
        var dispatcher = new StubDispatcher
        {
            QueryResult = query => query switch
            {
                GetProjectOverviewQuery overview => new ProjectOverviewView(
                    overview.ProjectId,
                    "DevContext",
                    "/srv/devcontext",
                    "Registered",
                    null,
                    null,
                    4,
                    3,
                    [new GodNodeView("node-1", "ProjectPage", 7)],
                    1),
                GetTimelineQuery timeline => new ActivityTimelineView(
                    timeline.ProjectId,
                    [new ActivityRecordView(
                        changeId, 9, "Cut over", "agent", "change", "", "abc123",
                        true, true, true, [], DateTimeOffset.Parse("2026-08-09T11:00:00Z"))]),
                GetProjectRequirementsQuery requirements => new RequirementsView(
                    requirements.ProjectId,
                    false,
                    "Use TDD.",
                    false,
                    DateTimeOffset.Parse("2026-08-09T12:30:00Z")),
                GetDecisionsQuery => new[]
                {
                    new DecisionView(
                        decisionId,
                        "Owner?",
                        "Projects",
                        "Service ownership",
                        DateTimeOffset.Parse("2026-08-09T12:00:00Z")),
                },
                _ => throw new InvalidOperationException($"Unexpected query {query.GetType().Name}"),
            },
        };

        var action = await Controller(dispatcher, "America/New_York").Get(projectId, default);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<ProjectPageResponse>(ok.Value);
        Assert.Equal(projectId, response.Overview.Id);
        Assert.Equal("DevContext", response.Overview.Name);
        Assert.Equal("ProjectPage", Assert.Single(response.Overview.GodNodes).Label);
        Assert.Equal(changeId, Assert.Single(response.Timeline!.Changes).Id);
        var decision = Assert.Single(response.Decisions!);
        Assert.Equal(decisionId, decision.Id);
        Assert.Equal("2026-08-09", decision.DecidedAtDisplay);
        Assert.Equal("Use TDD.", response.Requirements!.Markdown);
        Assert.Equal("2026-08-09 08:30", response.Requirements.UpdatedAtDisplay);
        Assert.Null(response.Message);
        Assert.Contains(dispatcher.Queries, query =>
            query is GetTimelineQuery { ProjectId: var id, Take: 8 } && id == projectId);
    }

    [Fact]
    public async Task Overview_context_keeps_overview_when_optional_query_fails()
    {
        var projectId = Guid.NewGuid();
        var dispatcher = new StubDispatcher
        {
            QueryResult = query => query switch
            {
                GetProjectOverviewQuery overview => new ProjectOverviewView(
                    overview.ProjectId, "DevContext", "/srv/devcontext", "Registered",
                    null, null, 0, 0, [], 0),
                GetTimelineQuery => throw new InvalidOperationException("timeline unavailable"),
                GetProjectRequirementsQuery requirements => RequirementsView.EmptyForProject(
                    requirements.ProjectId),
                GetDecisionsQuery => Array.Empty<DecisionView>(),
                _ => throw new InvalidOperationException(),
            },
        };

        var action = await Controller(dispatcher).Get(projectId, default);

        var response = Assert.IsType<ProjectPageResponse>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Null(response.Timeline);
        Assert.Empty(response.Decisions!);
        Assert.NotNull(response.Requirements);
        Assert.Equal("Could not load timeline: timeline unavailable", response.Message);
    }

    [Fact]
    public async Task Update_requirements_dispatches_projects_command_and_maps_null_markdown_as_empty()
    {
        var projectId = Guid.NewGuid();
        var updatedAt = DateTimeOffset.Parse("2026-08-09T12:30:00Z");
        var dispatcher = new StubDispatcher
        {
            CommandResult = command => command switch
            {
                SetProjectRequirementsCommand set => new RequirementsView(
                    set.ProjectId, false, set.Markdown, true, updatedAt),
                _ => throw new InvalidOperationException(),
            },
        };

        var action = await Controller(dispatcher, "America/New_York").UpdateRequirements(
            projectId,
            new UpdateProjectRequirementsRequest(null!),
            default);

        var response = Assert.IsType<ProjectPageRequirementsResponse>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        var command = Assert.IsType<SetProjectRequirementsCommand>(dispatcher.Commands.Single());
        Assert.Equal(projectId, command.ProjectId);
        Assert.Equal(string.Empty, command.Markdown);
        Assert.Equal(string.Empty, response.Markdown);
        Assert.Equal("2026-08-09 08:30", response.UpdatedAtDisplay);
    }

    [Fact]
    public void Controller_preserves_route_and_projects_view_authorization_contract()
    {
        var type = typeof(ProjectPageController);
        var route = Assert.Single(type.GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/v1/projects/{projectId:guid}", route.Template);
        var authorize = Assert.Single(type.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(ProjectsAuthenticationDefaults.ApiKeyScheme, authorize.AuthenticationSchemes);
        Assert.Equal(Permission.ProjectsView, authorize.Policy);

        Assert.Equal(
            "overview-context",
            Assert.Single(type.GetMethod(nameof(ProjectPageController.Get))!
                .GetCustomAttributes<HttpGetAttribute>()).Template);
        Assert.Equal(
            "requirements",
            Assert.Single(type.GetMethod(nameof(ProjectPageController.UpdateRequirements))!
                .GetCustomAttributes<HttpPutAttribute>()).Template);
    }

    private static ProjectPageController Controller(
        IDispatcher dispatcher,
        string timeZoneId = "UTC") => new(dispatcher, new StubCurrentTenant(timeZoneId))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private sealed class StubCurrentTenant(string timeZoneId) : ICurrentTenant
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public string Slug => "test";
        public string TimeZoneId => timeZoneId;
        public bool IsResolved => true;
    }

    private sealed class StubDispatcher : IDispatcher
    {
        public Func<object, object?>? CommandResult { get; init; }
        public Func<object, object?>? QueryResult { get; init; }
        public List<object> Commands { get; } = [];
        public List<object> Queries { get; } = [];

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            Commands.Add(command);
            return Task.FromResult((TResult)CommandResult!(command)!);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult((TResult)QueryResult!(query)!);
        }
    }
}

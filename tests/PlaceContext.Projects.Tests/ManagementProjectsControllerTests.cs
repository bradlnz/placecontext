using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Projects.Api;
using PlaceContext.Projects.Controllers;

namespace PlaceContext.Projects.Tests;

public sealed class ManagementProjectsControllerTests
{
    [Fact]
    public async Task List_returns_stable_management_responses()
    {
        var project = Project();
        var dispatcher = new StubDispatcher
        {
            QueryResult = _ => new[] { project },
        };
        var controller = Controller(dispatcher);

        var action = await controller.List();

        var response = Assert.IsType<OkObjectResult>(action.Result);
        var projects = Assert.IsAssignableFrom<IReadOnlyList<ProjectResponse>>(response.Value);
        var mapped = Assert.Single(projects);
        Assert.Equal(project.Id, mapped.Id);
        Assert.Equal(project.Name, mapped.Name);
        Assert.Equal(project.Path, mapped.Path);
        Assert.Equal(project.Status, mapped.Status);
        Assert.Equal(project.IsGraphified, mapped.IsGraphified);
    }

    [Fact]
    public async Task GetById_returns_not_found_when_project_is_missing()
    {
        object? captured = null;
        var dispatcher = new StubDispatcher
        {
            QueryResult = query =>
            {
                captured = query;
                return null;
            },
        };

        var action = await Controller(dispatcher).GetById(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(action.Result);
        Assert.IsType<PlaceContext.Application.Features.GetProjectByIdQuery>(captured);
    }

    [Fact]
    public async Task Create_rejects_blank_path_without_dispatching()
    {
        var dispatcher = new StubDispatcher();

        var action = await Controller(dispatcher).Create(new CreateProjectRequest(" ", "ignored"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.NotNull(badRequest.Value);
        Assert.Null(dispatcher.LastCommand);
    }

    [Fact]
    public async Task Create_dispatches_projects_command_and_returns_created_route()
    {
        var project = Project();
        var dispatcher = new StubDispatcher
        {
            CommandResult = _ => project,
        };

        var action = await Controller(dispatcher).Create(
            new CreateProjectRequest(project.Path, project.Name));

        var created = Assert.IsType<CreatedAtRouteResult>(action.Result);
        Assert.Equal(ManagementProjectsController.GetByIdRouteName, created.RouteName);
        Assert.Equal(project.Id, created.RouteValues!["id"]);
        var response = Assert.IsType<ProjectResponse>(created.Value);
        Assert.Equal(project.Id, response.Id);
        var command = Assert.IsType<PlaceContext.Application.Features.CreateProjectCommand>(
            dispatcher.LastCommand);
        Assert.Equal(project.Path, command.Path);
        Assert.Equal(project.Name, command.Name);
    }

    private static ManagementProjectsController Controller(IDispatcher dispatcher) => new(dispatcher)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static ProjectSummaryView Project() => new(
        Guid.NewGuid(), "DevContext", "/srv/devcontext", "Registered", true, 1, 2, 3);

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

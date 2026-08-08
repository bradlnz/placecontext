using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Controllers;

namespace PlaceContext.Application.Tests;

public sealed class JobOperationsControllerTests
{
    [Fact]
    public async Task GetJob_returns_the_public_jobs_contract()
    {
        var id = Guid.NewGuid();
        var dispatcher = new StubDispatcher { QueryResult = _ => Job(id) };
        var controller = new JobOperationsController(dispatcher);

        var result = await controller.GetJob(id, default);

        var response = Assert.IsType<JobResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(id, response.Id);
        Assert.Equal("nightly-report", response.Name);
        Assert.IsType<GetJobQuery>(dispatcher.LastQuery);
    }

    [Fact]
    public async Task UpdateJob_does_not_dispatch_when_the_job_is_missing()
    {
        var dispatcher = new StubDispatcher { QueryResult = _ => null };
        var controller = new JobOperationsController(dispatcher);

        var result = await controller.UpdateJob(Guid.NewGuid(), Request(), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Null(dispatcher.LastCommand);
    }

    [Fact]
    public async Task UpdateJob_returns_bad_request_for_an_invalid_contract_value()
    {
        var dispatcher = new StubDispatcher { QueryResult = _ => Job(Guid.NewGuid()) };
        var controller = new JobOperationsController(dispatcher);

        var result = await controller.UpdateJob(
            Guid.NewGuid(),
            Request() with { ReturnType = "not-a-return-type" },
            default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Null(dispatcher.LastCommand);
    }

    [Fact]
    public async Task DeleteJob_dispatches_to_jobs_and_returns_no_content()
    {
        var dispatcher = new StubDispatcher { CommandResult = _ => true };
        var controller = new JobOperationsController(dispatcher);
        var id = Guid.NewGuid();

        var result = await controller.DeleteJob(id, default);

        Assert.IsType<NoContentResult>(result);
        var command = Assert.IsType<DeleteJobCommand>(dispatcher.LastCommand);
        Assert.Equal(id, command.JobId);
    }

    private static JobRequest Request() => new(
        "nightly-report", null, "worker:latest", null, null, null, null,
        ["{}"], null, null, null, null, null, null, null);

    private static JobView Job(Guid id) => new(
        id, Guid.NewGuid(), "nightly-report", null,
        "image", "worker:latest", null, null, null, [], 1, ["{}"],
        new Dictionary<string, string>(),
        null, null, null, null, null, [], null,
        4, [0], [], false, false, [], [], JobReturnType.Json, null,
        0, 0, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class StubDispatcher : IDispatcher
    {
        public Func<object, object?>? CommandResult { get; init; }
        public Func<object, object?>? QueryResult { get; init; }
        public object? LastCommand { get; private set; }
        public object? LastQuery { get; private set; }

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            LastCommand = command;
            return Task.FromResult((TResult)CommandResult!(command)!);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult((TResult)QueryResult!(query)!);
        }
    }
}

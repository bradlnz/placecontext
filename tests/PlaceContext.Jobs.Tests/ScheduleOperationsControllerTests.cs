using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Jobs.Contracts.Management;
using PlaceContext.Jobs.Controllers;

namespace PlaceContext.Application.Tests;

public sealed class ScheduleOperationsControllerTests
{
    [Fact]
    public async Task Get_returns_the_public_schedule_contract()
    {
        var id = Guid.NewGuid();
        var dispatcher = new StubDispatcher { QueryResult = _ => Trigger(id) };
        var controller = new ScheduleOperationsController(dispatcher);

        var result = await controller.Get(id, default);

        var response = Assert.IsType<ScheduleResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(id, response.Id);
        Assert.Equal("nightly", response.Name);
        Assert.IsType<GetTriggerByIdQuery>(dispatcher.LastQuery);
    }

    [Fact]
    public async Task Update_does_not_dispatch_when_the_schedule_is_missing()
    {
        var dispatcher = new StubDispatcher { QueryResult = _ => null };
        var controller = new ScheduleOperationsController(dispatcher);

        var result = await controller.Update(Guid.NewGuid(), new UpdateScheduleRequest(Name: "new"), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Null(dispatcher.LastCommand);
    }

    [Fact]
    public async Task Delete_dispatches_to_jobs_and_returns_not_found_when_missing()
    {
        var dispatcher = new StubDispatcher { CommandResult = _ => false };
        var controller = new ScheduleOperationsController(dispatcher);
        var id = Guid.NewGuid();

        var result = await controller.Delete(id, default);

        Assert.IsType<NotFoundResult>(result);
        var command = Assert.IsType<DeleteTriggerCommand>(dispatcher.LastCommand);
        Assert.Equal(id, command.TriggerId);
    }

    private static TriggerView Trigger(Guid id) => new(
        id, Guid.NewGuid(), Guid.NewGuid(), "nightly", "Schedule", true,
        "0 0 * * *", null, null, null, null, null,
        DateTimeOffset.UtcNow.AddHours(1), null, DateTimeOffset.UtcNow);

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

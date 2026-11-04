using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class CancelChainRunTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_is_saved_before_cleanup_and_preserves_completed_output(bool cleanupFails)
    {
        var now = DateTimeOffset.UtcNow;
        var chain = JobChain.Create(Guid.NewGuid(), "cancel", null,
            new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() }, now);
        var run = ChainRun.Start(chain, new[] { "completed", "job", "action", "pending" }, now);
        run.MarkStepFinished(0, Guid.NewGuid(), ChainStepStatus.Succeeded, now);
        var child = Guid.NewGuid();
        run.MarkStepRunning(1, child, now);
        run.MarkStepRunning(2, null, now);
        run.SetCurrentPayload("{\"retained\":true}");
        var runs = new InMemoryChainRunRepository();
        await runs.AddAsync(run);
        var unit = new RecordingUnitOfWork();
        var dispatcher = new CancellationProbe(run, unit, cleanupFails);
        var handler = new CancelChainRunHandler(runs, unit, new FakeClock(now), dispatcher);

        Assert.True(await handler.HandleAsync(new CancelChainRunCommand(run.Id)));
        Assert.Equal(new[] { child }, dispatcher.Children);
        Assert.Equal(new[] { ChainRunStatus.Cancelled }, dispatcher.ObservedStatuses);
        Assert.Equal(new[] { 1 }, dispatcher.ObservedSaves);
        Assert.Equal(ChainStepStatus.Succeeded, run.Steps[0].Status);
        Assert.All(run.Steps.Skip(1), step => Assert.Equal(ChainStepStatus.Cancelled, step.Status));
        Assert.Equal("{\"retained\":true}", run.FinalOutput);
        Assert.NotNull(run.FinishedAt);

        Assert.True(await handler.HandleAsync(new CancelChainRunCommand(run.Id)));
        Assert.Single(dispatcher.Children);
        Assert.Equal(1, unit.SaveCount);
    }

    private sealed class CancellationProbe(ChainRun run, RecordingUnitOfWork unit, bool fails) : IDispatcher
    {
        public List<Guid> Children { get; } = [];
        public List<ChainRunStatus> ObservedStatuses { get; } = [];
        public List<int> ObservedSaves { get; } = [];

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            Children.Add(((CancelJobRunCommand)(object)command).RunId);
            ObservedStatuses.Add(run.Status);
            ObservedSaves.Add(unit.SaveCount);
            return fails
                ? Task.FromException<TResult>(new IOException("Workload node unavailable"))
                : Task.FromResult((TResult)(object)true);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}

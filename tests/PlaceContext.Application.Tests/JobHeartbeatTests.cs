using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;

namespace PlaceContext.Application.Tests;

public sealed class JobHeartbeatTests
{
    [Fact]
    public async Task Healthy_pulse_allows_shards_and_reduce_to_complete()
    {
        var runner = new Runner((_, _) => Task.FromResult(new WorkloadRunResult(0, "{}", "", "")));
        var (handler, job, _) = await Create(runner, new Pulse(() => true));
        var result = await handler.HandleAsync(new RunJobCommand(job.Id));
        Assert.Equal("Succeeded", result.Status);
        Assert.Equal(3, runner.Calls);
    }

    [Fact]
    public async Task Host_shutdown_cancels_the_record_and_waiting_work()
    {
        using var shutdown = new CancellationTokenSource();
        var runner = new Runner((_, ct) =>
        {
            shutdown.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Cancellation was not passed to the workload.");
        });
        var (handler, job, runs) = await Create(runner, new Pulse(() => true));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(new RunJobCommand(job.Id), shutdown.Token));
        Assert.Equal(1, runner.Calls);
        Assert.Equal(JobRunStatus.Cancelled, Assert.Single(await runs.ListForJobAsync(job.Id)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_initial_pulse_fails_without_starting_a_container(bool throws)
    {
        var runner = new Runner((_, _) => Task.FromResult(new WorkloadRunResult(0, null, "", "")));
        var (handler, job, runs) = await Create(runner, new Pulse(() => throws ? throw new IOException("database unavailable") : false));
        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(new RunJobCommand(job.Id)));
        Assert.Equal(0, runner.Calls);
        var run = Assert.Single(await runs.ListForJobAsync(job.Id));
        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task Lost_pulse_cancels_execution_and_prevents_waiting_shards_and_reduce()
    {
        var pulses = 0;
        var runner = new Runner(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new WorkloadRunResult(0, null, "", "");
        });
        var (handler, job, runs) = await Create(runner, new Pulse(() => ++pulses == 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(new RunJobCommand(job.Id)));
        Assert.Equal(1, runner.Calls);
        var run = Assert.Single(await runs.ListForJobAsync(job.Id));
        Assert.Equal(JobRunStatus.Failed, run.Status);
        Assert.Contains("keep-alive lost", Assert.Single(run.ShardResults).Log);
    }

    [Fact]
    public async Task Runner_exception_leaves_failed_history_instead_of_running_forever()
    {
        var runner = new Runner((_, _) => throw new IOException("container disappeared"));
        var (handler, job, runs) = await Create(runner, new Pulse(() => true));
        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(new RunJobCommand(job.Id)));
        Assert.Equal(JobRunStatus.Failed, Assert.Single(await runs.ListForJobAsync(job.Id)).Status);
    }

    [Fact]
    public async Task Cancellation_is_saved_before_containers_are_removed()
    {
        var runs = new InMemoryJobRunRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var map = new MapSpec("image", ["{}", "{}"], new Dictionary<string, string>());
        var run = JobRun.Start(Guid.NewGuid(), Guid.NewGuid(), clock.UtcNow, WorkloadSnapshot.From(map, null, 1));
        await runs.AddAsync(run);
        var runner = new Runner((_, _) => throw new InvalidOperationException());
        runner.OnCancel = _ => Assert.Equal(JobRunStatus.Cancelled, run.Status);
        await new CancelJobRunHandler(runs, runner, new RecordingUnitOfWork(), clock).HandleAsync(new(run.Id));
        Assert.Equal(2, runner.Cancels);
    }

    private static async Task<(RunJobHandler, Job, InMemoryJobRunRepository)> Create(IWorkloadRunner runner, IJobRunHeartbeat heartbeat)
    {
        var jobs = new InMemoryJobRepository();
        var runs = new InMemoryJobRunRepository();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var job = Job.Create(Guid.NewGuid(), "pulse", null,
            new MapSpec("image", ["{}", "{}"], new Dictionary<string, string>()),
            new ReduceSpec("reduce", new Dictionary<string, string>()), 1, ExitCodePolicy.Default, clock.UtcNow);
        await jobs.AddAsync(job);
        return (new RunJobHandler(jobs, runs, runner, new RecordingUnitOfWork(), clock, heartbeat: heartbeat), job, runs);
    }

    private sealed class Pulse(Func<bool> pulse) : IJobRunHeartbeat
    {
        public Task<bool> PulseAsync(Guid id, CancellationToken ct = default) => Task.FromResult(pulse());
    }

    private sealed class Runner(Func<WorkloadRunRequest, CancellationToken, Task<WorkloadRunResult>> run) : IWorkloadRunner
    {
        public int Calls, Cancels;
        public Action<string>? OnCancel;
        public Task<WorkloadRunResult> RunAsync(WorkloadRunRequest request, CancellationToken ct = default)
        {
            Calls++;
            return run(request, ct);
        }
        public Task CancelAsync(string id, CancellationToken ct = default)
        {
            Cancels++;
            OnCancel?.Invoke(id);
            return Task.CompletedTask;
        }
    }
}

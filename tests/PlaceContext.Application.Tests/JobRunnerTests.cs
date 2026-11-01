using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.TestSupport;
using Xunit;

namespace PlaceContext.Application.Tests;

/// <summary>
/// Unit tests for <see cref="JobRunner"/>, the per-job automatic retry orchestrator.
/// </summary>
public class JobRunnerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private static Job CreateJob(int retryCount = 0, int retryDelaySeconds = 0)
    {
        var map = new MapSpec("img", new[] { "{}" }, new Dictionary<string, string>());
        return Job.Create(Guid.NewGuid(), "retryable", null, map, null, 1, ExitCodePolicy.Default, T0,
            retryCount: retryCount, retryDelaySeconds: retryDelaySeconds);
    }

    private static JobRunDetailView DetailView(Guid runId, string status, Guid? originalRunId = null, int attempt = 1)
        => new(
            Id: runId,
            JobId: Guid.NewGuid(),
            ProjectId: Guid.NewGuid(),
            Status: status,
            StartedAt: T0,
            FinishedAt: T0.AddSeconds(1),
            ShardResults: Array.Empty<ShardResultView>(),
            ReduceResult: null,
            Snapshot: new JobRunSnapshotView("image", "img", null, null, null, 1, 1, false),
            AttemptNumber: attempt,
            OriginalRunId: originalRunId);

    [Fact]
    public async Task RunAsync_with_succeeded_first_attempt_returns_immediately()
    {
        var job = CreateJob();
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);

        var dispatcher = new RecordingDispatcher(runId => DetailView(runId, "Succeeded"));
        var runner = new JobRunner(jobs, dispatcher);

        var result = await runner.RunAsync(job.Id);

        Assert.Equal("Succeeded", result.Status);
        Assert.Single(dispatcher.Commands);
        Assert.Equal(1, dispatcher.Commands[0].AttemptNumber);
        Assert.Null(dispatcher.Commands[0].OriginalRunId);
    }

    [Fact]
    public async Task RunAsync_retries_failed_run_up_to_RetryCount_and_returns_last_result()
    {
        var job = CreateJob(retryCount: 2, retryDelaySeconds: 0);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);

        var attempt = 0;
        var originalRunId = Guid.Empty;
        var dispatcher = new RecordingDispatcher(runId =>
        {
            attempt++;
            if (attempt == 1)
            {
                originalRunId = runId;
                return DetailView(runId, "Failed", attempt: 1);
            }
            if (attempt == 2)
                return DetailView(runId, "Failed", originalRunId: originalRunId, attempt: 2);
            return DetailView(runId, "Succeeded", originalRunId: originalRunId, attempt: 3);
        });
        var runner = new JobRunner(jobs, dispatcher);

        var result = await runner.RunAsync(job.Id);

        Assert.Equal("Succeeded", result.Status);
        Assert.Equal(3, attempt);
        Assert.Equal(3, dispatcher.Commands.Count);
        Assert.Null(dispatcher.Commands[0].OriginalRunId);
        Assert.Equal(originalRunId, dispatcher.Commands[1].OriginalRunId);
        Assert.Equal(originalRunId, dispatcher.Commands[2].OriginalRunId);
        Assert.Equal(originalRunId, dispatcher.Commands[1].ReplayOfRunId);
        Assert.Equal(originalRunId, dispatcher.Commands[2].ReplayOfRunId);
        Assert.Equal(1, dispatcher.Commands[0].AttemptNumber);
        Assert.Equal(2, dispatcher.Commands[1].AttemptNumber);
        Assert.Equal(3, dispatcher.Commands[2].AttemptNumber);
    }

    [Fact]
    public async Task RunAsync_stops_after_RetryCount_exhausted_and_returns_failed()
    {
        var job = CreateJob(retryCount: 1, retryDelaySeconds: 0);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);

        var dispatcher = new RecordingDispatcher(runId => DetailView(runId, "Failed"));
        var runner = new JobRunner(jobs, dispatcher);

        var result = await runner.RunAsync(job.Id);

        Assert.Equal("Failed", result.Status);
        Assert.Equal(2, dispatcher.Commands.Count); // initial + 1 retry
    }

    [Fact]
    public async Task RunAsync_does_not_retry_Partial_status()
    {
        var job = CreateJob(retryCount: 2, retryDelaySeconds: 0);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);

        var dispatcher = new RecordingDispatcher(runId => DetailView(runId, "Partial"));
        var runner = new JobRunner(jobs, dispatcher);

        var result = await runner.RunAsync(job.Id);

        Assert.Equal("Partial", result.Status);
        Assert.Single(dispatcher.Commands);
    }

    [Fact]
    public async Task RunAsync_zero_RetryCount_does_not_retry()
    {
        var job = CreateJob(retryCount: 0, retryDelaySeconds: 0);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);

        var dispatcher = new RecordingDispatcher(runId => DetailView(runId, "Failed"));
        var runner = new JobRunner(jobs, dispatcher);

        var result = await runner.RunAsync(job.Id);

        Assert.Equal("Failed", result.Status);
        Assert.Single(dispatcher.Commands);
    }

    [Fact]
    public async Task RunAsync_passes_inputPayload_only_on_first_attempt()
    {
        var job = CreateJob(retryCount: 1, retryDelaySeconds: 0);
        var jobs = new InMemoryJobRepository();
        await jobs.AddAsync(job);

        var dispatcher = new RecordingDispatcher(runId => DetailView(runId, "Failed"));
        var runner = new JobRunner(jobs, dispatcher);

        await runner.RunAsync(job.Id, inputPayload: "{\"only\":\"first\"}");

        Assert.Equal("{\"only\":\"first\"}", dispatcher.Commands[0].InputPayload);
        Assert.Null(dispatcher.Commands[1].InputPayload);
    }

    [Fact]
    public async Task RunAsync_throws_when_job_not_found()
    {
        var jobs = new InMemoryJobRepository();
        var dispatcher = new RecordingDispatcher(_ => DetailView(Guid.NewGuid(), "Succeeded"));
        var runner = new JobRunner(jobs, dispatcher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Parallel_workflow_steps_use_independent_job_repositories()
    {
        // An actual workflow handler fans out several branches using its one
        // scoped JobRunner. The job metadata read must not reuse that handler's
        // DbContext; a concurrent access would throw (as PostgreSQL does).
        var job = CreateJob();
        var seen = new AccessCounter();
        var services = new ServiceCollection();
        services.AddScoped<IJobRepository>(_ => new ConcurrentReadGuardRepository(job, seen));
        await using var provider = services.BuildServiceProvider();
        var parentRepository = new ConcurrentReadGuardRepository(job, seen);
        var runner = new JobRunner(parentRepository,
            new SuccessOnlyDispatcher(), provider.GetRequiredService<IServiceScopeFactory>());

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => runner.RunWorkflowStepAsync(job.Id)));
        Assert.All(results, result => Assert.Equal("Succeeded", result.Status));
        Assert.Equal(8, seen.Value);
    }

    private sealed class AccessCounter { public int Value; }

    private sealed class ConcurrentReadGuardRepository(Job job, AccessCounter counter) : IJobRepository
    {
        private int _inFlight;
        public async Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _inFlight) != 1)
            {
                Interlocked.Decrement(ref _inFlight);
                throw new InvalidOperationException("Concurrent job reads shared one scoped repository.");
            }
            try
            {
                await Task.Delay(30, ct);
                Interlocked.Increment(ref counter.Value);
                return id == job.Id ? job : null;
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        }

        public Task AddAsync(Job item, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(Job item, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<Job>> ListForProjectAsync(Guid projectId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class SuccessOnlyDispatcher : IDispatcher
    {
        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            if (command is not RunJobCommand run) throw new NotSupportedException();
            return Task.FromResult((TResult)(object)DetailView(run.RunId ?? Guid.NewGuid(), "Succeeded"));
        }
        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RecordingDispatcher : IDispatcher
    {
        private readonly Func<Guid, JobRunDetailView> _factory;

        public RecordingDispatcher(Func<Guid, JobRunDetailView> factory)
        {
            _factory = factory;
        }

        public List<RunJobCommand> Commands { get; } = new();

        public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            if (command is not RunJobCommand runCmd)
                throw new NotSupportedException($"Unexpected command type: {command.GetType().Name}");

            Commands.Add(runCmd);
            var view = _factory(runCmd.RunId ?? Guid.NewGuid());
            return Task.FromResult((TResult)(object)view);
        }

        public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
            => throw new NotSupportedException("Queries are not used by JobRunner tests.");
    }
}

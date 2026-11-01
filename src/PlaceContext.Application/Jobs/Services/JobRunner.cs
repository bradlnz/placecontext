using Microsoft.Extensions.DependencyInjection;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <inheritdoc cref="IJobRunner"/>
public sealed class JobRunner : IJobRunner
{
    private readonly IJobRepository _jobs;
    private readonly IDispatcher _dispatcher;
    private readonly IServiceScopeFactory? _scopes;

    public JobRunner(IJobRepository jobs, IDispatcher dispatcher,
        IServiceScopeFactory? scopes = null)
    {
        _jobs = jobs;
        _dispatcher = dispatcher;
        _scopes = scopes;
    }

    private async Task<PlaceContext.Domain.Entities.Job?> LoadJobAsync(Guid id, CancellationToken ct)
    {
        if (_scopes is null)
            return await _jobs.GetByIdAsync(id, ct); // Lightweight unit tests.
        // A single workflow may dispatch multiple branches concurrently. The
        // JobRunner itself is scoped to the parent handler; its injected job
        // repository would share the parent's DbContext across parallel tasks.
        // Resolve job metadata in an independent scope for each branch instead.
        await using var scope = _scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IJobRepository>()
            .GetByIdAsync(id, ct);
    }

    public Task<JobRunDetailView> RunAsync(
        Guid jobId, string? inputPayload = null, Guid? runId = null,
        Guid? replayOfRunId = null, CancellationToken ct = default)
        => RunCoreAsync(jobId, inputPayload, runId, replayOfRunId, false, ct);

    public Task<JobRunDetailView> RunWorkflowStepAsync(
        Guid jobId, string? inputPayload = null, Guid? runId = null, CancellationToken ct = default)
        => RunCoreAsync(jobId, inputPayload, runId, null, true, ct);

    private async Task<JobRunDetailView> RunCoreAsync(
        Guid jobId, string? inputPayload, Guid? runId, Guid? replayOfRunId,
        bool isWorkflowStep, CancellationToken ct)
    {
        var job = await LoadJobAsync(jobId, ct)
            ?? throw new InvalidOperationException($"Job {jobId} not found.");

        var originalRunId = runId ?? Guid.NewGuid();
        var attemptNumber = 1;
        var currentRunId = originalRunId;

        while (true)
        {
            var result = await _dispatcher.Send(new RunJobCommand(
                jobId,
                attemptNumber == 1 ? inputPayload : null,
                currentRunId,
                attemptNumber == 1 ? replayOfRunId : originalRunId,
                attemptNumber,
                attemptNumber == 1 ? null : originalRunId,
                IsWorkflowStep: isWorkflowStep), ct);

            // Terminal statuses other than Failed (Succeeded/Partial) stop immediately.
            if (result.Status != "Failed" || attemptNumber > job.RetryCount)
                return result;

            // We have retry budget left; wait then attempt again with a fresh run id replaying
            // the original attempt's snapshot.
            if (job.RetryDelaySeconds > 0)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(job.RetryDelaySeconds), ct); }
                catch (OperationCanceledException) { throw; }
            }

            attemptNumber++;
            currentRunId = Guid.NewGuid();
        }
    }
}

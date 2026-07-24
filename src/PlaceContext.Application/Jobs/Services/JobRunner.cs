using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <inheritdoc cref="IJobRunner"/>
public sealed class JobRunner : IJobRunner
{
    private readonly IJobRepository _jobs;
    private readonly IDispatcher _dispatcher;

    public JobRunner(IJobRepository jobs, IDispatcher dispatcher)
    {
        _jobs = jobs;
        _dispatcher = dispatcher;
    }

    public async Task<JobRunDetailView> RunAsync(
        Guid jobId,
        string? inputPayload = null,
        Guid? runId = null,
        Guid? replayOfRunId = null,
        CancellationToken ct = default)
    {
        var job = await _jobs.GetByIdAsync(jobId, ct)
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
                attemptNumber == 1 ? null : originalRunId), ct);

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

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

/// <summary>
/// Replays a prior run: looks up its job, then re-executes the run's captured workload snapshot via
/// <see cref="RunJobCommand"/> (with <c>ReplayOfRunId</c> set). The dispatch runs in its own scope /
/// unit-of-work, exactly like any other job run.
/// </summary>
public sealed class ReplayRunHandler : ICommandHandler<ReplayRunCommand, JobRunDetailView>
{
    private readonly IJobRunRepository _runs;
    private readonly IDispatcher _dispatcher;

    public ReplayRunHandler(IJobRunRepository runs, IDispatcher dispatcher)
    {
        _runs = runs;
        _dispatcher = dispatcher;
    }

    public async Task<JobRunDetailView> HandleAsync(ReplayRunCommand command, CancellationToken ct = default)
    {
        var prior = await _runs.GetByIdAsync(command.RunId, ct)
            ?? throw new InvalidOperationException($"Run {command.RunId} not found — cannot replay.");

        return await _dispatcher.Send(
            new RunJobCommand(prior.JobId, RunId: command.NewRunId, ReplayOfRunId: command.RunId), ct);
    }
}

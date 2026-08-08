using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DeleteJobHandler : ICommandHandler<DeleteJobCommand, bool>
{
    private readonly IJobRepository _jobs;
    private readonly IJobsUnitOfWork _uow;

    public DeleteJobHandler(IJobRepository jobs, IJobsUnitOfWork uow)
    {
        _jobs = jobs;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteJobCommand command, CancellationToken ct = default)
    {
        var existing = await _jobs.GetByIdAsync(command.JobId, ct);
        if (existing is null) return false;

        await _jobs.RemoveAsync(command.JobId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

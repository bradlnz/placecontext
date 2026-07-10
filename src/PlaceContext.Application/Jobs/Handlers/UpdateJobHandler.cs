using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class UpdateJobHandler : ICommandHandler<UpdateJobCommand, JobView>
{
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateJobHandler(IJobRepository jobs, IUnitOfWork uow, IClock clock)
    {
        _jobs = jobs;
        _uow = uow;
        _clock = clock;
    }

    public async Task<JobView> HandleAsync(UpdateJobCommand command, CancellationToken ct = default)
    {
        var job = await _jobs.GetByIdAsync(command.JobId, ct)
            ?? throw new InvalidOperationException($"Job {command.JobId} not found.");

        var mapSource = WorkloadSourceResolver.ResolveRequired(
            command.MapImage, command.MapRuntimeId, command.MapSource, command.MapEntrypoint, command.MapFiles,
            parameterContext: "Map");

        var mapSpec = new MapSpec(mapSource, command.InputPayloads, command.MapEnv);

        ReduceSpec? reduceSpec = null;
        var reduceSource = WorkloadSourceResolver.Resolve(
            command.ReduceImage, command.ReduceRuntimeId, command.ReduceSource, command.ReduceEntrypoint, command.ReduceFiles,
            parameterContext: "Reduce");
        if (reduceSource is not null)
            reduceSpec = new ReduceSpec(reduceSource, command.ReduceEnv ?? new Dictionary<string, string>());

        var policy = new ExitCodePolicy(command.SuccessExitCodes, command.PartialExitCodes);

        job.Update(
            command.Name,
            command.Description,
            mapSpec,
            reduceSpec,
            command.ConcurrencyLimit,
            policy,
            _clock.UtcNow,
            allowNetworkEgress: command.AllowNetworkEgress,
            parameters: JobParameterMapper.ToDomain(command.Parameters),
            postJobActions: command.PostJobActions);

        await _jobs.UpdateAsync(job, ct);
        await _uow.SaveChangesAsync(ct);

        return JobViewMapper.ToView(job);
    }
}

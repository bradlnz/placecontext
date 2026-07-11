using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SaveDataMappingHandler : ICommandHandler<SaveDataMappingCommand, DataMappingView>
{
    private readonly IDataMappingRepository _mappings;
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SaveDataMappingHandler(IDataMappingRepository mappings, IJobRepository jobs, IUnitOfWork uow, IClock clock)
    {
        _mappings = mappings;
        _jobs = jobs;
        _uow = uow;
        _clock = clock;
    }

    public async Task<DataMappingView> HandleAsync(SaveDataMappingCommand command, CancellationToken ct = default)
    {
        var job = await _jobs.GetByIdAsync(command.JobId, ct)
            ?? throw new InvalidOperationException($"Job {command.JobId} not found.");
        if (job.ProjectId != command.ProjectId)
            throw new InvalidOperationException("The mapping's job must belong to the same project.");

        var fields = command.Fields
            .Select(f => new DataFieldMapping(f.SourcePath.Trim(), f.Column.Trim(), f.Type.Trim()).ThrowIfInvalid())
            .ToList();

        DataMapping mapping;
        if (command.MappingId is { } id)
        {
            mapping = await _mappings.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Data mapping {id} not found.");
            mapping.Update(command.TargetTable, command.RowsPath, fields, command.Enabled, _clock.UtcNow);
            await _mappings.UpdateAsync(mapping, ct);
        }
        else
        {
            mapping = DataMapping.Create(command.ProjectId, command.JobId, command.TargetTable,
                command.RowsPath, fields, _clock.UtcNow, command.Enabled);
            await _mappings.AddAsync(mapping, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return DataMappingViewMapper.ToView(mapping, job.Name);
    }
}

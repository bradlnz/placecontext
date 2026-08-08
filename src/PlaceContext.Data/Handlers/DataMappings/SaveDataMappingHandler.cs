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
    private readonly IJobChainRepository _chains;
    private readonly IDataUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly IProjectDataStore? _store;

    public SaveDataMappingHandler(IDataMappingRepository mappings, IJobRepository jobs,
        IJobChainRepository chains, IDataUnitOfWork uow, IClock clock, IProjectDataStore? store = null)
    {
        _mappings = mappings;
        _jobs = jobs;
        _chains = chains;
        _uow = uow;
        _clock = clock;
        _store = store;
    }

    public async Task<DataMappingView> HandleAsync(SaveDataMappingCommand command, CancellationToken ct = default)
    {
        // The source is a job's runs or a chain's final output — either way it must exist in
        // this project.
        string sourceName;
        if (command.SourceKind == "chain")
        {
            var chain = await _chains.GetByIdAsync(command.JobId, ct)
                ?? throw new InvalidOperationException($"Chain {command.JobId} not found.");
            if (chain.ProjectId != command.ProjectId)
                throw new InvalidOperationException("The mapping's chain must belong to the same project.");
            sourceName = chain.Name;
        }
        else
        {
            var job = await _jobs.GetByIdAsync(command.JobId, ct)
                ?? throw new InvalidOperationException($"Job {command.JobId} not found.");
            if (job.ProjectId != command.ProjectId)
                throw new InvalidOperationException("The mapping's job must belong to the same project.");
            sourceName = job.Name;
        }

        var fields = command.Fields
            .Select(f => new DataFieldMapping(f.SourcePath.Trim(), f.Column.Trim(), f.Type.Trim()).ThrowIfInvalid())
            .ToList();

        // Guard the silent-drop failure at its source: if the target table already exists, a field
        // pointed at a column it doesn't have would fail every insert at ingest time (the run still
        // reports "Succeeded" while the whole batch vanishes). Reject it here, up front, where the
        // user can act — a not-yet-created table is unconstrained (it's built from these fields).
        if (_store is not null)
        {
            var existing = await _store.ListColumnsAsync(command.ProjectId, command.TargetTable.Trim(), ct);
            if (existing.Count > 0)
            {
                var have = existing.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missing = fields.Select(f => f.Column).Where(c => !have.Contains(c)).Distinct().ToList();
                if (missing.Count > 0)
                    throw new InvalidOperationException(
                        $"Table '{command.TargetTable.Trim()}' has no column(s) {string.Join(", ", missing.Select(m => $"'{m}'"))}. " +
                        $"Its columns are: {string.Join(", ", existing.Select(c => c.Name))}. " +
                        "Point the field(s) at an existing column, or add the column on the Data tab first.");
            }
        }

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
                command.RowsPath, fields, _clock.UtcNow, command.Enabled, command.SourceKind);
            await _mappings.AddAsync(mapping, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return DataMappingViewMapper.ToView(mapping, sourceName);
    }
}

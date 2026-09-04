using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SaveDataEntityHandler : ICommandHandler<SaveDataEntityCommand, DataEntityView>
{
    private readonly IDataEntityRepository _entities;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SaveDataEntityHandler(IDataEntityRepository entities, IUnitOfWork uow, IClock clock)
    {
        _entities = entities;
        _uow = uow;
        _clock = clock;
    }

    public async Task<DataEntityView> HandleAsync(SaveDataEntityCommand command, CancellationToken ct = default)
    {
        ProjectDataReservedNames.EnsureAllowed(command.Name, "entity name");
        ProjectDataReservedNames.EnsureAllowed(command.TableName, "table name");

        var relations = command.Relations
            .Where(r => !string.IsNullOrWhiteSpace(r.Column) && !string.IsNullOrWhiteSpace(r.TargetEntity))
            .Select(r => new EntityRelation(r.Column.Trim(), r.TargetEntity.Trim(),
                string.IsNullOrWhiteSpace(r.TargetColumn) ? r.Column.Trim() : r.TargetColumn.Trim()))
            .ToList();

        var tags = command.Tags ?? Array.Empty<string>();

        DataEntity entity;
        if (command.EntityId is { } id)
        {
            entity = await _entities.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Entity {id} not found.");
            entity.Update(command.Name, command.TableName, command.LabelColumn, relations, _clock.UtcNow, tags);
            await _entities.UpdateAsync(entity, ct);
        }
        else
        {
            entity = DataEntity.Create(command.ProjectId, command.Name, command.TableName,
                command.LabelColumn, relations, _clock.UtcNow, tags);
            await _entities.AddAsync(entity, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return DataEntityMapper.ToView(entity);
    }
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.ProjectData;
using PlaceContext.Application.Shared;
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
        async Task<DataEntity> SaveEntityAsync()
        {
            DataEntity resolvedEntity;
            if (command.EntityId is { } id)
            {
                resolvedEntity = await _entities.GetByIdAsync(id, ct)
                    ?? throw new InvalidOperationException($"Entity {id} not found.");
                resolvedEntity.Update(command.Name, command.TableName, command.LabelColumn, relations, _clock.UtcNow, tags);
                await _entities.UpdateAsync(resolvedEntity, ct);
                return resolvedEntity;
            }
            resolvedEntity = DataEntity.Create(command.ProjectId, command.Name, command.TableName,
                command.LabelColumn, relations, _clock.UtcNow, tags);
            await _entities.AddAsync(resolvedEntity, ct);
            return resolvedEntity;
        }
        entity = await SaveEntityAsync();
        await _uow.SaveChangesAsync(ct);
        return DataEntityMapper.ToView(entity);
    }
}

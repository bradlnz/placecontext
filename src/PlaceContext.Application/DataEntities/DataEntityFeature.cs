using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>One relation edge of the entity graph: this column ↔ that entity's column.</summary>
public sealed record EntityRelationDto(string Column, string TargetEntity, string TargetColumn);

/// <summary>Read model for a business entity tag (node of the project's data graph).</summary>
public sealed record DataEntityView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string TableName,
    string? LabelColumn,
    IReadOnlyList<EntityRelationDto> Relations,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Creates or updates a business entity: tags a table/view of ingested data with a name, a label
/// column, and its relations to other entities — the nodes and edges of the project's data graph.
/// Null <paramref name="EntityId"/> creates.
/// </summary>
public sealed record SaveDataEntityCommand(
    Guid ProjectId, string Name, string TableName, string? LabelColumn,
    IReadOnlyList<EntityRelationDto> Relations, Guid? EntityId = null) : ICommand<DataEntityView>;

public sealed record DeleteDataEntityCommand(Guid EntityId) : ICommand<bool>;

public sealed record ListDataEntitiesQuery(Guid ProjectId) : IQuery<IReadOnlyList<DataEntityView>>;

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
        var relations = command.Relations
            .Where(r => !string.IsNullOrWhiteSpace(r.Column) && !string.IsNullOrWhiteSpace(r.TargetEntity))
            .Select(r => new EntityRelation(r.Column.Trim(), r.TargetEntity.Trim(),
                string.IsNullOrWhiteSpace(r.TargetColumn) ? r.Column.Trim() : r.TargetColumn.Trim()))
            .ToList();

        DataEntity entity;
        if (command.EntityId is { } id)
        {
            entity = await _entities.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Entity {id} not found.");
            entity.Update(command.Name, command.TableName, command.LabelColumn, relations, _clock.UtcNow);
            await _entities.UpdateAsync(entity, ct);
        }
        else
        {
            entity = DataEntity.Create(command.ProjectId, command.Name, command.TableName,
                command.LabelColumn, relations, _clock.UtcNow);
            await _entities.AddAsync(entity, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return DataEntityMapper.ToView(entity);
    }
}

public sealed class DeleteDataEntityHandler : ICommandHandler<DeleteDataEntityCommand, bool>
{
    private readonly IDataEntityRepository _entities;
    private readonly IUnitOfWork _uow;

    public DeleteDataEntityHandler(IDataEntityRepository entities, IUnitOfWork uow)
    {
        _entities = entities;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteDataEntityCommand command, CancellationToken ct = default)
    {
        await _entities.DeleteAsync(command.EntityId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class ListDataEntitiesHandler : IQueryHandler<ListDataEntitiesQuery, IReadOnlyList<DataEntityView>>
{
    private readonly IDataEntityRepository _entities;

    public ListDataEntitiesHandler(IDataEntityRepository entities) => _entities = entities;

    public async Task<IReadOnlyList<DataEntityView>> HandleAsync(ListDataEntitiesQuery query, CancellationToken ct = default)
        => (await _entities.ListForProjectAsync(query.ProjectId, ct)).Select(DataEntityMapper.ToView).ToList();
}

internal static class DataEntityMapper
{
    public static DataEntityView ToView(DataEntity e) => new(
        e.Id, e.ProjectId, e.Name, e.TableName, e.LabelColumn,
        e.Relations.Select(r => new EntityRelationDto(r.Column, r.TargetEntity, r.TargetColumn)).ToList(),
        e.UpdatedAt);
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

internal static class DataEntityMapper
{
    public static DataEntityView ToView(DataEntity e) => new(
        e.Id, e.ProjectId, e.Name, e.TableName, e.LabelColumn,
        e.Relations.Select(r => new EntityRelationDto(r.Column, r.TargetEntity, r.TargetColumn)).ToList(),
        e.Tags,
        e.UpdatedAt);
}

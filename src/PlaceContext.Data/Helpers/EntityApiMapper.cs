using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Data.Contracts.Api;

namespace PlaceContext.Data.Helpers;

internal static class EntityApiMapper
{
    public static EntityApiResponse ToResponse(DataEntityView entity) => new(
        entity.Id, entity.ProjectId, entity.Name, entity.TableName, entity.LabelColumn,
        ProjectDataReservedNames.Slug(entity.Name),
        entity.Relations.Select(relation => new EntityRelationResponse(
            relation.Column, relation.TargetEntity, relation.TargetColumn)).ToList(),
        entity.Tags.ToList(), entity.UpdatedAt);

    public static EntityRecordsResponse ToRecords(DataEntityView entity, ProjectTablePageResult result) => new(
        entity.Id, entity.Name, entity.TableName, result.Columns.ToList(),
        result.Rows.Select(row => (IReadOnlyList<string?>)row.ToList()).ToList(),
        result.TotalCount, result.Page, result.PageSize);
}

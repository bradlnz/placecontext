using PlaceContext.Application.Features;
using PlaceContext.Application.ProjectData;

namespace PlaceContext.Host.Controllers.Api;

public static class EntityApiMapper
{
    public static EntityApiResponse ToResponse(DataEntityView e) => new(
        e.Id, e.ProjectId, e.Name, e.TableName, e.LabelColumn,
        ProjectDataReservedNames.Slug(e.Name),
        e.Relations.Select(r => new EntityRelationResponse(r.Column, r.TargetEntity, r.TargetColumn)).ToList(),
        e.Tags.ToList(), e.UpdatedAt);

    public static EntityRecordsResponse ToRecords(DataEntityView e, ProjectTablePageResult result) => new(
        e.Id, e.Name, e.TableName,
        result.Columns.ToList(),
        result.Rows.Select(r => r.ToList()).ToList(),
        result.TotalCount, result.Page, result.PageSize);
}

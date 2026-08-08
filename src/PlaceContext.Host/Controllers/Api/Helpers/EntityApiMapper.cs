using PlaceContext.Host.Controllers.Api.Records;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
namespace PlaceContext.Host.Controllers.Api.Helpers;

public static class EntityApiMapper
{
    public static EntityApiResponse ToResponse(DataEntityView dataEntityView) => new(
        dataEntityView.Id, 
        dataEntityView.ProjectId, 
        dataEntityView.Name, 
        dataEntityView.TableName, 
        dataEntityView.LabelColumn,
        ProjectDataReservedNames.Slug(dataEntityView.Name),
        [.. dataEntityView.Relations.Select(r => 
            new EntityRelationResponse(r.Column, r.TargetEntity, r.TargetColumn)
        )],
        [.. dataEntityView.Tags], 
        dataEntityView.UpdatedAt
    );

    public static EntityRecordsResponse ToRecords(DataEntityView dataEntityView, ProjectTablePageResult result) => new(
        dataEntityView.Id, 
        dataEntityView.Name, 
        dataEntityView.TableName,
        [.. result.Columns],
        result.Rows.Select(
            r => r.ToList()
        ).ToList(),
        result.TotalCount, 
        result.Page, 
        result.PageSize
    );
}

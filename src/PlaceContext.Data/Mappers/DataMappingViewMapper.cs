using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Entities;

namespace PlaceContext.Application.Features;

internal static class DataMappingViewMapper
{
    public static DataMappingView ToView(DataMapping m, string jobName) => new(
        Id: m.Id,
        ProjectId: m.ProjectId,
        JobId: m.JobId,
        JobName: jobName,
        SourceKind: m.SourceKind,
        TargetTable: m.TargetTable,
        RowsPath: m.RowsPath,
        Fields: m.Fields.Select(f => new DataFieldDto(f.SourcePath, f.Column, f.Type)).ToList(),
        Enabled: m.Enabled,
        CreatedAt: m.CreatedAt,
        UpdatedAt: m.UpdatedAt);
}

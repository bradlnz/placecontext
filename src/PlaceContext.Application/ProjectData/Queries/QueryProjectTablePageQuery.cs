using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>A server-side paginated, searchable page of one project table's rows — the entity
/// browser's Records tab. <paramref name="Search"/>, when set, matches case-insensitively across
/// every column (bound as a parameter, never concatenated into SQL).</summary>
public sealed record QueryProjectTablePageQuery(
    Guid ProjectId, string TableName, string? Search, int Page, int PageSize) : IQuery<ProjectTablePageResult>;

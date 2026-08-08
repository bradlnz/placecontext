using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Export a whole table as CSV.</summary>
public sealed record ExportProjectTableQuery(Guid ProjectId, string TableName) : IQuery<string>;

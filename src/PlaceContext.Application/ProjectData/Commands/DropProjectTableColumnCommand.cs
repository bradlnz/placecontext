using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Drop a column from an existing table in a project's database.</summary>
public sealed record DropProjectTableColumnCommand(Guid ProjectId, string TableName, string ColumnName) : ICommand<bool>;

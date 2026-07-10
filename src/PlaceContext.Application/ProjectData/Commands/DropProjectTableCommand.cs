using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Drop a table from a project's database.</summary>
public sealed record DropProjectTableCommand(Guid ProjectId, string TableName) : ICommand<bool>;

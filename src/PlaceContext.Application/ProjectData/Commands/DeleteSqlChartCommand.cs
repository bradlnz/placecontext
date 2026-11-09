using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>Removes a user-defined SQL chart.</summary>
public sealed record DeleteSqlChartCommand(Guid ProjectId, string Name) : ICommand<bool>;

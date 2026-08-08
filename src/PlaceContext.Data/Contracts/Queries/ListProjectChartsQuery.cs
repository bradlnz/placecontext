using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

/// <summary>The project's stored analytics charts (produced by the background sweep).</summary>
public sealed record ListProjectChartsQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectChartView>>;

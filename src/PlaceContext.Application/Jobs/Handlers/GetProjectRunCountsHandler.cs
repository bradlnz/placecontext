using PlaceContext.Application.Cqrs;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class GetProjectRunCountsHandler(IJobRunRepository runs) : IQueryHandler<GetProjectRunCountsQuery, IReadOnlyDictionary<string, int>>
{
    public Task<IReadOnlyDictionary<string, int>> HandleAsync(GetProjectRunCountsQuery query, CancellationToken ct = default)
        => runs.CountByStatusAsync(query.ProjectId, DateTimeOffset.UtcNow.AddHours(-24), ct);
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record ListJobTestCasesQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<JobTestCaseView>>;

public sealed record GetJobTestCaseQuery(Guid TestId)
    : IQuery<JobTestCaseView?>;

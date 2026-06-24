using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetDebtDashboardHandler : IQueryHandler<GetDebtDashboardQuery, DebtDashboardView>
{
    private readonly IDebtAssessmentRepository _assessments;
    public GetDebtDashboardHandler(IDebtAssessmentRepository assessments) => _assessments = assessments;

    public async Task<DebtDashboardView> HandleAsync(GetDebtDashboardQuery query, CancellationToken ct = default)
    {
        var latest = await _assessments.GetLatestAsync(ProjectId.From(query.ProjectId), ct);
        return ViewMapper.ToDashboard(latest);
    }
}

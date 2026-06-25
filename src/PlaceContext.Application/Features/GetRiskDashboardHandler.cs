using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRiskDashboardHandler : IQueryHandler<GetRiskDashboardQuery, RiskDashboardView>
{
    private readonly IRiskAssessmentRepository _assessments;
    public GetRiskDashboardHandler(IRiskAssessmentRepository assessments) => _assessments = assessments;

    public async Task<RiskDashboardView> HandleAsync(GetRiskDashboardQuery query, CancellationToken ct = default)
    {
        var latest = await _assessments.GetLatestAsync(ProjectId.From(query.ProjectId), ct);
        return ViewMapper.ToDashboard(latest);
    }
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetCostDashboardHandler : IQueryHandler<GetCostDashboardQuery, CostDashboardView>
{
    private readonly IUsageRepository _usage;
    private readonly TokenCostCalculator _cost;

    public GetCostDashboardHandler(IUsageRepository usage, TokenCostCalculator cost)
    {
        _usage = usage;
        _cost = cost;
    }

    public async Task<CostDashboardView> HandleAsync(GetCostDashboardQuery query, CancellationToken ct = default)
    {
        var records = await _usage.ListForProjectAsync(ProjectId.From(query.ProjectId), ct);

        var byModel = records
            .GroupBy(r => r.Usage.Model)
            .Select(g => new ModelCostView(
                g.Key, g.Sum(r => r.Usage.InputTokens), g.Sum(r => r.Usage.OutputTokens), g.Sum(r => _cost.CostUsd(r.Usage))))
            .OrderByDescending(m => m.CostUsd)
            .ToList();

        var recent = records
            .OrderByDescending(r => r.RecordedAt)
            .Take(20)
            .Select(r => ViewMapper.ToView(r, _cost.CostUsd(r.Usage)))
            .ToList();

        return new CostDashboardView(
            query.ProjectId,
            records.Sum(r => r.Usage.InputTokens),
            records.Sum(r => r.Usage.OutputTokens),
            records.Sum(r => _cost.CostUsd(r.Usage)),
            byModel, recent);
    }
}

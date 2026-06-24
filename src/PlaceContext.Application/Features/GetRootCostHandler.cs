using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class GetRootCostHandler : IQueryHandler<GetRootCostQuery, RootCostView>
{
    private readonly IUsageRepository _usage;
    private readonly IProjectRepository _projects;
    private readonly TokenCostCalculator _cost;

    public GetRootCostHandler(IUsageRepository usage, IProjectRepository projects, TokenCostCalculator cost)
    {
        _usage = usage;
        _projects = projects;
        _cost = cost;
    }

    public async Task<RootCostView> HandleAsync(GetRootCostQuery query, CancellationToken ct = default)
    {
        var records = await _usage.ListAllAsync(ct);
        var names = (await _projects.ListAsync(ct)).ToDictionary(p => p.Id, p => p.Name.Value);

        var byModel = records
            .GroupBy(r => r.Usage.Model)
            .Select(g => new ModelCostView(
                g.Key, g.Sum(r => r.Usage.InputTokens), g.Sum(r => r.Usage.OutputTokens), g.Sum(r => _cost.CostUsd(r.Usage))))
            .OrderByDescending(m => m.CostUsd)
            .ToList();

        var byProject = records
            .GroupBy(r => r.ProjectId)
            .Select(g => new ProjectCostView(
                g.Key.Value, names.GetValueOrDefault(g.Key, "(unknown)"),
                g.Sum(r => r.Usage.Total), g.Sum(r => _cost.CostUsd(r.Usage))))
            .OrderByDescending(p => p.CostUsd)
            .ToList();

        return new RootCostView(
            records.Sum(r => r.Usage.InputTokens),
            records.Sum(r => r.Usage.OutputTokens),
            records.Sum(r => _cost.CostUsd(r.Usage)),
            records.Count,
            byModel, byProject);
    }
}

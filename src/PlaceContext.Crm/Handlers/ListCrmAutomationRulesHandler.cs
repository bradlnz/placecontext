using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ListCrmAutomationRulesHandler
    : IQueryHandler<ListCrmAutomationRulesQuery, IReadOnlyList<CrmAutomationRuleView>>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;

    public ListCrmAutomationRulesHandler(
        ICrmAutomationRuleRepository rules, IJobChainRepository chains, IJobRepository jobs)
        => (_rules, _chains, _jobs) = (rules, chains, jobs);

    public async Task<IReadOnlyList<CrmAutomationRuleView>> HandleAsync(
        ListCrmAutomationRulesQuery query, CancellationToken ct = default)
    {
        var values = new List<CrmAutomationRuleView>();
        foreach (var rule in await _rules.ListForProjectAsync(query.ProjectId, ct))
            values.Add(await SaveCrmAutomationRuleHandler.MapAsync(
                rule, await _chains.GetByIdAsync(rule.ChainId, ct), _jobs, ct));
        return values;
    }
}

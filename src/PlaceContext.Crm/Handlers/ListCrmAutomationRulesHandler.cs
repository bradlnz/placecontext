using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class ListCrmAutomationRulesHandler
    : IQueryHandler<ListCrmAutomationRulesQuery, IReadOnlyList<CrmAutomationRuleView>>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly ICrmJobsClient _jobs;

    public ListCrmAutomationRulesHandler(
        ICrmAutomationRuleRepository rules, ICrmJobsClient jobs)
        => (_rules, _jobs) = (rules, jobs);

    public async Task<IReadOnlyList<CrmAutomationRuleView>> HandleAsync(
        ListCrmAutomationRulesQuery query, CancellationToken ct = default)
    {
        var chains = (await _jobs.GetCatalogAsync(query.ProjectId, ct)).Chains
            .ToDictionary(chain => chain.Id);
        var values = new List<CrmAutomationRuleView>();
        foreach (var rule in await _rules.ListForProjectAsync(query.ProjectId, ct))
        {
            chains.TryGetValue(rule.ChainId, out var chain);
            values.Add(SaveCrmAutomationRuleHandler.Map(rule, chain));
        }
        return values;
    }
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SetCrmAutomationEnabledHandler
    : ICommandHandler<SetCrmAutomationEnabledCommand, CrmAutomationRuleView>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly ICrmJobsClient _jobs;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;

    public SetCrmAutomationEnabledHandler(
        ICrmAutomationRuleRepository rules, ICrmJobsClient jobs,
        ICrmUnitOfWork uow, IClock clock)
        => (_rules, _jobs, _uow, _clock) = (rules, jobs, uow, clock);

    public async Task<CrmAutomationRuleView> HandleAsync(
        SetCrmAutomationEnabledCommand command, CancellationToken ct = default)
    {
        var rule = await _rules.GetByIdAsync(command.RuleId, ct)
            ?? throw new InvalidOperationException($"Automation {command.RuleId} not found.");
        rule.SetEnabled(command.Enabled, _clock.UtcNow);
        await _rules.UpdateAsync(rule, ct);
        await _uow.SaveChangesAsync(ct);
        var chain = (await _jobs.GetCatalogAsync(rule.ProjectId, ct)).Chains
            .FirstOrDefault(candidate => candidate.Id == rule.ChainId);
        return SaveCrmAutomationRuleHandler.Map(rule, chain);
    }
}

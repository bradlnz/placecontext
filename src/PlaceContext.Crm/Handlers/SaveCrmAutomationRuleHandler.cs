using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SaveCrmAutomationRuleHandler
    : ICommandHandler<SaveCrmAutomationRuleCommand, CrmAutomationRuleView>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly ICrmJobsClient _jobs;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;

    public SaveCrmAutomationRuleHandler(
        ICrmAutomationRuleRepository rules, ICrmJobsClient jobs,
        ICrmUnitOfWork uow, IClock clock)
        => (_rules, _jobs, _uow, _clock) = (rules, jobs, uow, clock);

    public async Task<CrmAutomationRuleView> HandleAsync(
        SaveCrmAutomationRuleCommand command, CancellationToken ct = default)
    {
        var chain = (await _jobs.GetCatalogAsync(command.ProjectId, ct)).Chains
            .FirstOrDefault(candidate => candidate.Id == command.ChainId)
            ?? throw new InvalidOperationException($"Job chain {command.ChainId} not found.");
        if (chain.ProjectId != command.ProjectId)
            throw new InvalidOperationException("The automation chain must belong to this project.");

        var lifecycleStage = command.EventType == CrmAutomationEventType.IngestionReceived
            ? null
            : command.LifecycleStage;
        CrmAutomationRule rule;
        if (command.RuleId is { } id)
        {
            rule = await _rules.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Automation {id} not found.");
            if (rule.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Automation does not belong to this project.");
            rule.Update(command.Name, command.EventType, lifecycleStage,
                command.ChainId, command.Enabled, _clock.UtcNow);
            await _rules.UpdateAsync(rule, ct);
        }
        else
        {
            rule = CrmAutomationRule.Create(
                command.ProjectId, command.Name, command.EventType, lifecycleStage,
                command.ChainId, command.Enabled, _clock.UtcNow);
            await _rules.AddAsync(rule, ct);
        }
        await _uow.SaveChangesAsync(ct);
        return Map(rule, chain);
    }

    internal static CrmAutomationRuleView Map(
        CrmAutomationRule rule,
        CrmJobChainSummary? chain)
    {
        return new CrmAutomationRuleView(
            rule.Id, rule.ProjectId, rule.Name, rule.EventType.ToString(),
            rule.LifecycleStage?.ToString(), rule.ChainId, chain?.Name ?? "Deleted job chain",
            chain?.StepCount ?? 0, rule.Enabled, rule.UpdatedAt);
    }
}

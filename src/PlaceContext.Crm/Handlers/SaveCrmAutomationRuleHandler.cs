using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SaveCrmAutomationRuleHandler
    : ICommandHandler<SaveCrmAutomationRuleCommand, CrmAutomationRuleView>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;

    public SaveCrmAutomationRuleHandler(
        ICrmAutomationRuleRepository rules, IJobChainRepository chains, IJobRepository jobs,
        ICrmUnitOfWork uow, IClock clock)
        => (_rules, _chains, _jobs, _uow, _clock) = (rules, chains, jobs, uow, clock);

    public async Task<CrmAutomationRuleView> HandleAsync(
        SaveCrmAutomationRuleCommand command, CancellationToken ct = default)
    {
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
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
        return await MapAsync(rule, chain, _jobs, ct);
    }

    internal static async Task<CrmAutomationRuleView> MapAsync(
        CrmAutomationRule rule, JobChain? chain, IJobRepository jobs, CancellationToken ct)
    {
        var stepCount = chain?.ExecutionStepCount ?? 0;
        // Resolve at least one step to distinguish a deleted chain from an empty display.
        if (chain?.StepJobIds is { Count: > 0 } jobIds) _ = await jobs.GetByIdAsync(jobIds[0], ct);
        return new CrmAutomationRuleView(
            rule.Id, rule.ProjectId, rule.Name, rule.EventType.ToString(),
            rule.LifecycleStage?.ToString(), rule.ChainId, chain?.Name ?? "Deleted job chain",
            stepCount, rule.Enabled, rule.UpdatedAt);
    }
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SaveCrmAutomationRuleHandler
    : ICommandHandler<SaveCrmAutomationRuleCommand, CrmAutomationRuleView>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SaveCrmAutomationRuleHandler(
        ICrmAutomationRuleRepository rules, IJobChainRepository chains, IJobRepository jobs,
        IUnitOfWork uow, IClock clock)
        => (_rules, _chains, _jobs, _uow, _clock) = (rules, chains, jobs, uow, clock);

    public async Task<CrmAutomationRuleView> HandleAsync(
        SaveCrmAutomationRuleCommand command, CancellationToken ct = default)
    {
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Job chain {command.ChainId} not found.");
        if (chain.ProjectId != command.ProjectId)
            throw new InvalidOperationException("The automation chain must belong to this project.");

        CrmAutomationRule rule;
        if (command.RuleId is { } id)
        {
            rule = await _rules.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Automation {id} not found.");
            if (rule.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Automation does not belong to this project.");
            rule.Update(command.Name, command.EventType, command.LifecycleStage,
                command.ChainId, command.Enabled, _clock.UtcNow);
            await _rules.UpdateAsync(rule, ct);
        }
        else
        {
            rule = CrmAutomationRule.Create(
                command.ProjectId, command.Name, command.EventType, command.LifecycleStage,
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

public sealed class SetCrmAutomationEnabledHandler
    : ICommandHandler<SetCrmAutomationEnabledCommand, CrmAutomationRuleView>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly IJobChainRepository _chains;
    private readonly IJobRepository _jobs;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SetCrmAutomationEnabledHandler(
        ICrmAutomationRuleRepository rules, IJobChainRepository chains, IJobRepository jobs,
        IUnitOfWork uow, IClock clock)
        => (_rules, _chains, _jobs, _uow, _clock) = (rules, chains, jobs, uow, clock);

    public async Task<CrmAutomationRuleView> HandleAsync(
        SetCrmAutomationEnabledCommand command, CancellationToken ct = default)
    {
        var rule = await _rules.GetByIdAsync(command.RuleId, ct)
            ?? throw new InvalidOperationException($"Automation {command.RuleId} not found.");
        rule.SetEnabled(command.Enabled, _clock.UtcNow);
        await _rules.UpdateAsync(rule, ct);
        await _uow.SaveChangesAsync(ct);
        return await SaveCrmAutomationRuleHandler.MapAsync(
            rule, await _chains.GetByIdAsync(rule.ChainId, ct), _jobs, ct);
    }
}

public sealed class DeleteCrmAutomationRuleHandler
    : ICommandHandler<DeleteCrmAutomationRuleCommand, bool>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly IUnitOfWork _uow;

    public DeleteCrmAutomationRuleHandler(ICrmAutomationRuleRepository rules, IUnitOfWork uow)
        => (_rules, _uow) = (rules, uow);

    public async Task<bool> HandleAsync(
        DeleteCrmAutomationRuleCommand command, CancellationToken ct = default)
    {
        if (await _rules.GetByIdAsync(command.RuleId, ct) is null) return false;
        await _rules.RemoveAsync(command.RuleId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

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

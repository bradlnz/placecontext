using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

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

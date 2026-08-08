using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class DeleteCrmAutomationRuleHandler
    : ICommandHandler<DeleteCrmAutomationRuleCommand, bool>
{
    private readonly ICrmAutomationRuleRepository _rules;
    private readonly ICrmUnitOfWork _uow;

    public DeleteCrmAutomationRuleHandler(ICrmAutomationRuleRepository rules, ICrmUnitOfWork uow)
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

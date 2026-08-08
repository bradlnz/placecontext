using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record SaveCrmAutomationRuleCommand(
    Guid ProjectId,
    string Name,
    CrmAutomationEventType EventType,
    CustomerLifecycleStage? LifecycleStage,
    Guid ChainId,
    bool Enabled = true,
    Guid? RuleId = null) : ICommand<CrmAutomationRuleView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

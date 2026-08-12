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
    public string RequiredPermission => Permission.CrmAutomationManage;
}

public sealed record SetCrmAutomationEnabledCommand(Guid RuleId, bool Enabled)
    : ICommand<CrmAutomationRuleView>, IRequiresPermission
{
    public string RequiredPermission => Permission.CrmAutomationManage;
}

public sealed record DeleteCrmAutomationRuleCommand(Guid RuleId)
    : ICommand<bool>, IRequiresPermission
{
    public string RequiredPermission => Permission.CrmAutomationManage;
}

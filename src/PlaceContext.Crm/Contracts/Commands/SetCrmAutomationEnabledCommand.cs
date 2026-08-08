using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed record SetCrmAutomationEnabledCommand(Guid RuleId, bool Enabled)
    : ICommand<CrmAutomationRuleView>, IRequiresPermission
{
    public string RequiredPermission => Permission.DataWrite;
}

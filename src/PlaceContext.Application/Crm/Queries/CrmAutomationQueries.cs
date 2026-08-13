using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed record ListCrmAutomationRulesQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<CrmAutomationRuleView>>,
    IRequiresPermission
{
    public string RequiredPermission => Permission.CrmView;
}

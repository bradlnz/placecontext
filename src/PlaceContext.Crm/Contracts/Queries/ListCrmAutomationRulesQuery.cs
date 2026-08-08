using PlaceContext.Application.Cqrs;

namespace PlaceContext.Application.Features;

public sealed record ListCrmAutomationRulesQuery(Guid ProjectId)
    : IQuery<IReadOnlyList<CrmAutomationRuleView>>;

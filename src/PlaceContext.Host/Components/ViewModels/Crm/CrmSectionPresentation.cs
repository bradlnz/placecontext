using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels.Crm;

public sealed record CrmSectionPresentation(
    CrmSection Section,
    string Title,
    string Description,
    bool CanAdd,
    string AddLabel
);

namespace PlaceContext.Host.Controllers;

public sealed record CustomerPortalJobChainView(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    IReadOnlyList<CustomerPortalJobChainStepView> Steps);

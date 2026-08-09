using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Crm.Contracts.Api;

public sealed record SaveCustomerPortalClientRequest(
    Guid ProjectId,
    string Name,
    string? Company,
    string? Email,
    string? Phone,
    CustomerLifecycleStage LifecycleStage,
    string? Notes)
{
    public SaveCrmClientCommand ToCommand(Guid? id = null)
        => new(ProjectId, Name, Company, Email, Phone, LifecycleStage, Notes, id);
}

using PlaceContext.Application.Features;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Controllers;

public sealed record SaveClientRequest(
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

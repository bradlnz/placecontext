using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Organization;
using PlaceContext.Application.Shared;
namespace PlaceContext.Application.Features;

public sealed class SetTenantBrandingHandler(ITenantBrandingStore store, IUnitOfWork unitOfWork)
    : ICommandHandler<SetTenantBrandingCommand, bool>
{
    public async Task<bool> HandleAsync(SetTenantBrandingCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command.Branding);
        await store.SetAsync(command.Branding, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}

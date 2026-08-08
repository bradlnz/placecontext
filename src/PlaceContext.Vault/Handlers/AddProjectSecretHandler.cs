using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Vault.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class AddProjectSecretHandler : ICommandHandler<AddProjectSecretCommand, ProjectSecretView>
{
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;
    private readonly IVaultUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AddProjectSecretHandler(
        IProjectSecretRepository secrets,
        ISecretProtector protector,
        IVaultUnitOfWork unitOfWork,
        IClock clock)
        => (_secrets, _protector, _unitOfWork, _clock) = (secrets, protector, unitOfWork, clock);

    public async Task<ProjectSecretView> HandleAsync(AddProjectSecretCommand c, CancellationToken ct = default)
    {
        var name = (c.Name ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("Secret name is required.");
        var now = _clock.UtcNow;
        await _secrets.AddAsync(c.ProjectId, name, _protector.Protect(c.Value ?? ""), now, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return new ProjectSecretView(name, now);
    }
}

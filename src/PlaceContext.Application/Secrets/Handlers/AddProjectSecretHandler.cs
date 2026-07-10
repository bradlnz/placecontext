using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class AddProjectSecretHandler : ICommandHandler<AddProjectSecretCommand, ProjectSecretView>
{
    private readonly IProjectSecretRepository _secrets;
    private readonly ISecretProtector _protector;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public AddProjectSecretHandler(IProjectSecretRepository secrets, ISecretProtector protector, IUnitOfWork uow, IClock clock)
        => (_secrets, _protector, _uow, _clock) = (secrets, protector, uow, clock);

    public async Task<ProjectSecretView> HandleAsync(AddProjectSecretCommand c, CancellationToken ct = default)
    {
        var name = (c.Name ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("Secret name is required.");
        var now = _clock.UtcNow;
        await _secrets.AddAsync(c.ProjectId, name, _protector.Protect(c.Value ?? ""), now, ct);
        await _uow.SaveChangesAsync(ct);
        return new ProjectSecretView(name, now);
    }
}

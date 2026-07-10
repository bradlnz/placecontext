using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SetGlobalRequirementsHandler : ICommandHandler<SetGlobalRequirementsCommand, RequirementsView>
{
    private readonly IRequirementsRepository _requirements;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SetGlobalRequirementsHandler(IRequirementsRepository requirements, IUnitOfWork uow, IClock clock)
    {
        _requirements = requirements;
        _uow = uow;
        _clock = clock;
    }

    public async Task<RequirementsView> HandleAsync(SetGlobalRequirementsCommand command, CancellationToken ct = default)
    {
        var doc = await _requirements.GetGlobalAsync(ct) ?? Requirements.StartGlobal(_clock.UtcNow);
        doc.Replace(command.Markdown, _clock.UtcNow);
        await _requirements.SaveAsync(doc, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(doc);
    }
}

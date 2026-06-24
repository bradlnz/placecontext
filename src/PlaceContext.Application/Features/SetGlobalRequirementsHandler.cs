using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SetGlobalRequirementsHandler : ICommandHandler<SetGlobalRequirementsCommand, CodeRequirementsView>
{
    private readonly ICodeRequirementsRepository _requirements;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SetGlobalRequirementsHandler(ICodeRequirementsRepository requirements, IUnitOfWork uow, IClock clock)
    {
        _requirements = requirements;
        _uow = uow;
        _clock = clock;
    }

    public async Task<CodeRequirementsView> HandleAsync(SetGlobalRequirementsCommand command, CancellationToken ct = default)
    {
        var doc = await _requirements.GetGlobalAsync(ct) ?? CodeRequirements.StartGlobal(_clock.UtcNow);
        doc.Replace(command.Markdown, _clock.UtcNow);
        await _requirements.SaveAsync(doc, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(doc);
    }
}

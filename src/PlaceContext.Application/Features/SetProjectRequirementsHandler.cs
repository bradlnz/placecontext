using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class SetProjectRequirementsHandler : ICommandHandler<SetProjectRequirementsCommand, CodeRequirementsView>
{
    private readonly ICodeRequirementsRepository _requirements;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public SetProjectRequirementsHandler(ICodeRequirementsRepository requirements, IUnitOfWork uow, IClock clock)
    {
        _requirements = requirements;
        _uow = uow;
        _clock = clock;
    }

    public async Task<CodeRequirementsView> HandleAsync(SetProjectRequirementsCommand command, CancellationToken ct = default)
    {
        var projectId = ProjectId.From(command.ProjectId);
        var doc = await _requirements.GetForProjectAsync(projectId, ct)
            ?? CodeRequirements.StartForProject(projectId, _clock.UtcNow);
        doc.Replace(command.Markdown, _clock.UtcNow);
        await _requirements.SaveAsync(doc, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(doc);
    }
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

public sealed class AddDecisionHandler : ICommandHandler<AddDecisionCommand, DecisionView>
{
    private readonly IDecisionRepository _decisions;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public AddDecisionHandler(IDecisionRepository decisions, IUnitOfWork uow, IClock clock)
    {
        _decisions = decisions;
        _uow = uow;
        _clock = clock;
    }

    public async Task<DecisionView> HandleAsync(AddDecisionCommand command, CancellationToken ct = default)
    {
        var decision = Decision.Record(
            ProjectId.From(command.ProjectId),
            command.Question, command.Choice,
            Rationale.OrNone(command.Rationale), _clock.UtcNow);

        await _decisions.AddAsync(decision, ct);
        await _uow.SaveChangesAsync(ct);
        return ViewMapper.ToView(decision);
    }
}

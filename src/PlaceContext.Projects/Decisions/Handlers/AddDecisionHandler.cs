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
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AddDecisionHandler(
        IDecisionRepository decisions,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _decisions = decisions;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<DecisionView> HandleAsync(
        AddDecisionCommand command,
        CancellationToken ct = default)
    {
        var decision = Decision.Record(
            ProjectId.From(command.ProjectId),
            command.Question,
            command.Choice,
            Rationale.OrNone(command.Rationale),
            _clock.UtcNow);

        await _decisions.AddAsync(decision, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return ViewMapper.ToView(decision);
    }
}

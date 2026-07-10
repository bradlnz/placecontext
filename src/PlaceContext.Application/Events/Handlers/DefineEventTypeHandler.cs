using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class DefineEventTypeHandler : ICommandHandler<DefineEventTypeCommand, EventTypeView>
{
    private readonly IEventRepository _events;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public DefineEventTypeHandler(IEventRepository events, IUnitOfWork uow, IClock clock)
    {
        _events = events;
        _uow = uow;
        _clock = clock;
    }

    public async Task<EventTypeView> HandleAsync(DefineEventTypeCommand command, CancellationToken ct = default)
    {
        var existing = await _events.FindDefinitionByNameAsync(command.Name.Trim(), ct);
        EventDefinition definition;
        if (existing is not null)
        {
            existing.Update(command.Description, command.PayloadSchema, _clock.UtcNow);
            await _events.UpdateDefinitionAsync(existing, ct);
            definition = existing;
        }
        else
        {
            definition = EventDefinition.Create(command.Name, command.Description, command.PayloadSchema, _clock.UtcNow);
            await _events.AddDefinitionAsync(definition, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return new EventTypeView(definition.Name, definition.Description, IsBuiltIn: false,
            definition.PayloadSchema, definition.CreatedAt);
    }
}

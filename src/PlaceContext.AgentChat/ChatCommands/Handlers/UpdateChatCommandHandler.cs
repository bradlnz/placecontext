using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class UpdateChatCommandHandler : ICommandHandler<UpdateChatCommandCommand, ChatCommandView>
{
    private readonly IChatCommandRepository _repo;
    private readonly IAgentChatUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateChatCommandHandler(IChatCommandRepository repo, IAgentChatUnitOfWork uow, IClock clock)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ChatCommandView> HandleAsync(UpdateChatCommandCommand command, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(command.Id, ct)
            ?? throw new InvalidOperationException($"ChatCommand {command.Id} not found.");
        entity.Update(command.Name, command.Description, command.ToolName, command.Args, _clock.UtcNow);
        await _repo.UpdateAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ChatCommandMapper.ToView(entity);
    }
}

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateChatCommandHandler : ICommandHandler<CreateChatCommandCommand, ChatCommandView>
{
    private readonly IChatCommandRepository _repo;
    private readonly IAgentChatUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateChatCommandHandler(IChatCommandRepository repo, IAgentChatUnitOfWork uow, IClock clock)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
    }

    public async Task<ChatCommandView> HandleAsync(CreateChatCommandCommand command, CancellationToken ct = default)
    {
        var entity = ChatCommand.Create(command.ProjectId, command.Name, command.Description,
            command.ToolName, command.Args, _clock.UtcNow);
        await _repo.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return ChatCommandMapper.ToView(entity);
    }
}

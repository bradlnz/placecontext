using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CreateChatCommandHandler : ICommandHandler<CreateChatCommandCommand, ChatCommandView>
{
    private readonly IChatCommandRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public CreateChatCommandHandler(IChatCommandRepository repo, IUnitOfWork uow, IClock clock)
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

public sealed class UpdateChatCommandHandler : ICommandHandler<UpdateChatCommandCommand, ChatCommandView>
{
    private readonly IChatCommandRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public UpdateChatCommandHandler(IChatCommandRepository repo, IUnitOfWork uow, IClock clock)
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

public sealed class DeleteChatCommandHandler : ICommandHandler<DeleteChatCommandCommand, bool>
{
    private readonly IChatCommandRepository _repo;
    private readonly IUnitOfWork _uow;

    public DeleteChatCommandHandler(IChatCommandRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<bool> HandleAsync(DeleteChatCommandCommand command, CancellationToken ct = default)
    {
        var entity = await _repo.GetByIdAsync(command.Id, ct);
        if (entity is null) return false;
        await _repo.RemoveAsync(command.Id, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class ListChatCommandsHandler : IQueryHandler<ListChatCommandsQuery, IReadOnlyList<ChatCommandView>>
{
    private readonly IChatCommandRepository _repo;

    public ListChatCommandsHandler(IChatCommandRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ChatCommandView>> HandleAsync(ListChatCommandsQuery query, CancellationToken ct = default)
    {
        var entities = await _repo.ListForProjectAsync(query.ProjectId, ct);
        return entities.Select(ChatCommandMapper.ToView).ToList();
    }
}

internal static class ChatCommandMapper
{
    internal static ChatCommandView ToView(ChatCommand c) => new(
        c.Id, c.ProjectId, c.Name, c.Description,
        c.ToolName, c.Args, c.CreatedAt, c.UpdatedAt);
}

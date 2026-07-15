using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class CreateEntityRecordHandler : ICommandHandler<CreateEntityRecordCommand, bool>
{
    private readonly IProjectDataStore _store;
    public CreateEntityRecordHandler(IProjectDataStore store) => _store = store;

    public async Task<bool> HandleAsync(CreateEntityRecordCommand command, CancellationToken ct = default)
    {
        await _store.InsertRowAsync(command.ProjectId, command.TableName, command.Values, ct);
        return true;
    }
}

public sealed class UpdateEntityRecordHandler : ICommandHandler<UpdateEntityRecordCommand, int>
{
    private readonly IProjectDataStore _store;
    public UpdateEntityRecordHandler(IProjectDataStore store) => _store = store;

    public Task<int> HandleAsync(UpdateEntityRecordCommand command, CancellationToken ct = default)
        => _store.UpdateRowsAsync(command.ProjectId, command.TableName, command.Keys, command.Values, ct);
}

public sealed class DeleteEntityRecordHandler : ICommandHandler<DeleteEntityRecordCommand, int>
{
    private readonly IProjectDataStore _store;
    public DeleteEntityRecordHandler(IProjectDataStore store) => _store = store;

    public Task<int> HandleAsync(DeleteEntityRecordCommand command, CancellationToken ct = default)
        => _store.DeleteRowsAsync(command.ProjectId, command.TableName, command.Keys, ct);
}

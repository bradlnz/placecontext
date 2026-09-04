using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class DeleteEntityRecordHandler : ICommandHandler<DeleteEntityRecordCommand, int>
{
    private readonly IProjectDataStore _store;
    private readonly RecordLinkService? _links;
    private readonly ILogger<DeleteEntityRecordHandler>? _log;

    public DeleteEntityRecordHandler(IProjectDataStore store, RecordLinkService? links = null,
        ILogger<DeleteEntityRecordHandler>? log = null)
    {
        _store = store;
        _links = links;
        _log = log;
    }

    public async Task<int> HandleAsync(DeleteEntityRecordCommand command, CancellationToken ct = default)
    {
        var affected = await _store.DeleteRowsAsync(command.ProjectId, command.TableName, command.Keys, ct);
        if (affected > 0)
            await RecordLinkHook.RefreshAsync(_links, command.ProjectId, command.TableName, _log, ct);
        return affected;
    }
}

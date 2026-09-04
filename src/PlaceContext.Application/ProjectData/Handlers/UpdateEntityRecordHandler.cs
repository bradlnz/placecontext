using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class UpdateEntityRecordHandler : ICommandHandler<UpdateEntityRecordCommand, int>
{
    private readonly IProjectDataStore _store;
    private readonly RecordLinkService? _links;
    private readonly ILogger<UpdateEntityRecordHandler>? _log;

    public UpdateEntityRecordHandler(IProjectDataStore store, RecordLinkService? links = null,
        ILogger<UpdateEntityRecordHandler>? log = null)
    {
        _store = store;
        _links = links;
        _log = log;
    }

    public async Task<int> HandleAsync(UpdateEntityRecordCommand command, CancellationToken ct = default)
    {
        var affected = await _store.UpdateRowsAsync(command.ProjectId, command.TableName, command.Keys, command.Values, ct);
        if (affected > 0)
            await RecordLinkHook.RefreshAsync(_links, command.ProjectId, command.TableName, _log, ct);
        return affected;
    }
}

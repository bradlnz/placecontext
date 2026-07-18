using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class CreateEntityRecordHandler : ICommandHandler<CreateEntityRecordCommand, CreateEntityRecordResult>
{
    private readonly IProjectDataStore _store;
    private readonly RecordLinkService? _links;
    private readonly ILogger<CreateEntityRecordHandler>? _log;

    public CreateEntityRecordHandler(IProjectDataStore store, RecordLinkService? links = null,
        ILogger<CreateEntityRecordHandler>? log = null)
    {
        _store = store;
        _links = links;
        _log = log;
    }

    public async Task<CreateEntityRecordResult> HandleAsync(CreateEntityRecordCommand command, CancellationToken ct = default)
    {
        var warnings = await FindDuplicatesAsync(command, ct);
        await _store.InsertRowAsync(command.ProjectId, command.TableName, command.Values, ct);
        await RecordLinkHook.RefreshAsync(_links, command.ProjectId, command.TableName, _log, ct);
        return new CreateEntityRecordResult(warnings);
    }

    // The duplicate check runs BEFORE the insert (after it, the row would match itself); warn-only.
    private async Task<IReadOnlyList<string>> FindDuplicatesAsync(CreateEntityRecordCommand command, CancellationToken ct)
    {
        if (_links is null) return Array.Empty<string>();
        try
        {
            return await _links.FindDuplicatesAsync(command.ProjectId, command.TableName, new[] { command.Values }, ct: ct);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Duplicate check for '{Table}' failed — the create continues.", command.TableName);
            return Array.Empty<string>();
        }
    }
}

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

/// <summary>The write-path hook every record mutation shares: refresh the table's link slice,
/// best-effort — a failure is logged and never changes the outcome of the write.</summary>
internal static class RecordLinkHook
{
    public static async Task RefreshAsync(RecordLinkService? links, Guid projectId, string table,
        ILogger? log, CancellationToken ct)
    {
        if (links is null) return;
        try
        {
            await links.RefreshTableAsync(projectId, table, ct);
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Record-link refresh of '{Table}' failed — the write is unaffected.", table);
        }
    }
}

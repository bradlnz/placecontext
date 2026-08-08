using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

public sealed class SaveSavedQueryHandler
    : ICommandHandler<SaveSavedQueryCommand, SavedQueryRecord>
{
    private readonly ISavedQueryStore _store;
    private readonly IClock _clock;

    public SaveSavedQueryHandler(ISavedQueryStore store, IClock clock)
        => (_store, _clock) = (store, clock);

    public async Task<SavedQueryRecord> HandleAsync(
        SaveSavedQueryCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Query name is required.", nameof(command));
        if (string.IsNullOrWhiteSpace(command.Sql))
            throw new ArgumentException("Query SQL is required.", nameof(command));

        var name = command.Name.Trim();
        var existing = await _store.FindByNameAsync(command.ProjectId, name, ct);
        var now = _clock.UtcNow;
        var item = new SavedQueryRecord(
            existing?.Id ?? Guid.NewGuid(),
            command.ProjectId,
            name,
            command.Sql.Trim(),
            existing?.CreatedAt ?? now,
            now);
        await _store.SaveAsync(item, ct);
        return item;
    }
}

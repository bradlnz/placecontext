using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class CompleteOcrHandler : ICommandHandler<CompleteOcrCommand, bool>
{
    private readonly IRunArtifactLinkRepository _links;
    private readonly OcrResultStorageService _storage;
    private readonly IClock _clock;
    private readonly IArtifactsUnitOfWork _uow;
    private readonly ILogger<CompleteOcrHandler>? _log;

    public CompleteOcrHandler(
        IRunArtifactLinkRepository links,
        OcrResultStorageService storage,
        IClock clock,
        IArtifactsUnitOfWork uow,
        ILogger<CompleteOcrHandler>? log = null)
    {
        _links = links;
        _storage = storage;
        _clock = clock;
        _uow = uow;
        _log = log;
    }

    public async Task<bool> HandleAsync(CompleteOcrCommand command, CancellationToken ct = default)
    {
        // GetByIdAsync respects the tenant global filter — a caller can only complete OCR for an
        // artifact that belongs to its own workspace.
        var link = await _links.GetByIdAsync(command.ArtifactId, ct);
        if (link is null) return false;

        // On success, persist the extraction first; the tracking update below commits both in the
        // same transaction. A storage failure still marks the artifact processed-with-error so it
        // leaves the pending queue and the operator can see why. (External-DB projects have no
        // platform-owned read-only tables, so AppendReadOnlyRowsAsync throws NotSupportedException
        // there — that lands in the same processed-with-error bucket rather than wedging the queue.)
        var error = command.Error;
        if (string.IsNullOrWhiteSpace(command.Error) && !string.IsNullOrWhiteSpace(command.Markdown))
        {
            try
            {
                await _storage.StoreAsync(link, command.Markdown, ct);
            }
            catch (Exception ex)
            {
                _log?.LogError(ex, "Storing OCR result for artifact {ArtifactId} failed.", link.Id);
                error = $"storage failed: {ex.Message}";
            }
        }

        await _links.MarkOcrProcessedAsync(link.Id, _clock.UtcNow, error, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

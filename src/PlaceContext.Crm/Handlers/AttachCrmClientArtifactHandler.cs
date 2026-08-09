using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;
using PlaceContext.Crm.Automation;
using PlaceContext.Crm.Integration;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class AttachCrmClientArtifactHandler
    : ICommandHandler<AttachCrmClientArtifactCommand, CrmClientArtifactView>
{
    public const int MaxFileBytes = 20 * 1024 * 1024;
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly ICrmArtifactsClient _storage;
    private readonly ICrmUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public AttachCrmClientArtifactHandler(
        ICrmClientRepository clients,
        ICrmClientArtifactRepository artifacts,
        ICrmArtifactsClient storage,
        ICrmUnitOfWork uow,
        IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _artifacts, _storage, _uow, _clock, _automations)
            = (clients, artifacts, storage, uow, clock, automations);

    public async Task<CrmClientArtifactView> HandleAsync(
        AttachCrmClientArtifactCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        if (command.Content.Length == 0) throw new ArgumentException("Choose a non-empty file.");
        if (command.Content.Length > MaxFileBytes)
            throw new ArgumentException($"Files must be {MaxFileBytes / 1024 / 1024} MB or smaller.");

        var id = Guid.NewGuid();
        var title = SafeFileName(command.FileName);
        // Keep the object-store key opaque: the user-visible filename is encrypted in the CRM
        // artifact row, and file bytes are encrypted by IObjectStore itself.
        var stored = await _storage.StoreAsync(
            client.ProjectId,
            client.Id,
            id,
            command.Content,
            command.ContentType ?? "application/octet-stream",
            ct);
        var value = CrmClientArtifact.CreateUpload(
            id, client.ProjectId, client.Id, title, stored.Bucket, stored.ObjectKey,
            command.ContentType ?? "application/octet-stream", command.Content.LongLength, _clock.UtcNow);

        try
        {
            await _artifacts.AddAsync(value, ct);
            if (_automations is not null)
                await _automations.EnqueueAsync(
                    client, Domain.ValueObjects.CrmAutomationEventType.ArtifactAttached, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch
        {
            await _storage.DeleteAsync(value.Bucket, value.ObjectKey, ct);
            throw;
        }
        return CrmClientArtifactMapper.ToView(value);
    }

    private static string SafeFileName(string value)
    {
        var name = Path.GetFileName(value);
        if (string.IsNullOrWhiteSpace(name)) name = "attachment";
        return new string(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
    }
}

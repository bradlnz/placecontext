using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class AttachCrmClientArtifactHandler
    : ICommandHandler<AttachCrmClientArtifactCommand, CrmClientArtifactView>
{
    public const int MaxFileBytes = 20 * 1024 * 1024;
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly IObjectStore _store;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public AttachCrmClientArtifactHandler(
        ICrmClientRepository clients,
        ICrmClientArtifactRepository artifacts,
        IObjectStore store,
        IUnitOfWork uow,
        IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _artifacts, _store, _uow, _clock, _automations)
            = (clients, artifacts, store, uow, clock, automations);

    public async Task<CrmClientArtifactView> HandleAsync(
        AttachCrmClientArtifactCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        if (!_store.IsEnabled) throw new InvalidOperationException("File storage is not configured.");
        if (command.Content.Length == 0) throw new ArgumentException("Choose a non-empty file.");
        if (command.Content.Length > MaxFileBytes)
            throw new ArgumentException($"Files must be {MaxFileBytes / 1024 / 1024} MB or smaller.");

        var id = Guid.NewGuid();
        var title = SafeFileName(command.FileName);
        // Keep the object-store key opaque: the user-visible filename is encrypted in the CRM
        // artifact row, and file bytes are encrypted by IObjectStore itself.
        var key = $"crm-clients/{client.ProjectId:N}/{client.Id:N}/{id:N}/content";
        var value = CrmClientArtifact.CreateUpload(
            id, client.ProjectId, client.Id, title, _store.ReportsBucket, key,
            command.ContentType ?? "application/octet-stream", command.Content.LongLength, _clock.UtcNow);

        await _store.PutAsync(value.Bucket, value.ObjectKey, command.Content, value.ContentType, ct);
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
            await _store.DeleteAsync(value.Bucket, value.ObjectKey, ct);
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

using System.Text.Json;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Application.Features;

public sealed class SaveCrmClientHandler : ICommandHandler<SaveCrmClientCommand, CrmClientView>
{
    private readonly ICrmClientRepository _clients;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public SaveCrmClientHandler(
        ICrmClientRepository clients, IUnitOfWork uow, IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _uow, _clock, _automations) = (clients, uow, clock, automations);

    public async Task<CrmClientView> HandleAsync(SaveCrmClientCommand command, CancellationToken ct = default)
    {
        CrmClient client;
        if (command.ClientId is { } id)
        {
            client = await _clients.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"Client {id} not found.");
            if (client.ProjectId != command.ProjectId)
                throw new InvalidOperationException("Client does not belong to this project.");
            var previousStage = client.LifecycleStage;
            client.Update(command.Name, command.Company, command.Email, command.Phone,
                command.LifecycleStage, command.Notes, _clock.UtcNow);
            await _clients.UpdateAsync(client, ct);
            if (_automations is not null)
            {
                await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.ClientUpdated, ct);
                if (previousStage != client.LifecycleStage)
                    await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.StageEntered, ct);
            }
        }
        else
        {
            client = CrmClient.Create(command.ProjectId, command.Name, command.Company, command.Email,
                command.Phone, command.LifecycleStage, command.Notes, _clock.UtcNow);
            await _clients.AddAsync(client, ct);
            if (_automations is not null)
            {
                await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.ClientCreated, ct);
                await _automations.EnqueueAsync(client, Domain.ValueObjects.CrmAutomationEventType.StageEntered, ct);
            }
        }

        await _uow.SaveChangesAsync(ct);
        return CrmClientMapper.ToView(client);
    }
}

public sealed class MoveCrmClientHandler : ICommandHandler<MoveCrmClientCommand, CrmClientView>
{
    private readonly ICrmClientRepository _clients;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public MoveCrmClientHandler(
        ICrmClientRepository clients, IUnitOfWork uow, IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _uow, _clock, _automations) = (clients, uow, clock, automations);

    public async Task<CrmClientView> HandleAsync(MoveCrmClientCommand command, CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var previousStage = client.LifecycleStage;
        client.MoveTo(command.LifecycleStage, _clock.UtcNow);
        await _clients.UpdateAsync(client, ct);
        if (_automations is not null && previousStage != client.LifecycleStage)
            await _automations.EnqueueAsync(
                client, Domain.ValueObjects.CrmAutomationEventType.StageEntered, ct);
        await _uow.SaveChangesAsync(ct);
        return CrmClientMapper.ToView(client);
    }
}

public sealed class DeleteCrmClientHandler : ICommandHandler<DeleteCrmClientCommand, bool>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly IObjectStore _store;
    private readonly IUnitOfWork _uow;

    public DeleteCrmClientHandler(
        ICrmClientRepository clients,
        ICrmClientArtifactRepository artifacts,
        IObjectStore store,
        IUnitOfWork uow)
        => (_clients, _artifacts, _store, _uow) = (clients, artifacts, store, uow);

    public async Task<bool> HandleAsync(DeleteCrmClientCommand command, CancellationToken ct = default)
    {
        if (await _clients.GetByIdAsync(command.ClientId, ct) is null) return false;
        foreach (var artifact in await _artifacts.ListForClientAsync(command.ClientId, 1000, ct))
            if (artifact.IsDirectUpload)
                await _store.DeleteAsync(artifact.Bucket, artifact.ObjectKey, ct);
        await _clients.DeleteAsync(command.ClientId, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class ConfigureCrmClientPortalHandler
    : ICommandHandler<ConfigureCrmClientPortalCommand, CrmClientView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICurrentTenant _tenant;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ICustomerPortalProvisioner _provisioner;

    public ConfigureCrmClientPortalHandler(
        ICrmClientRepository clients,
        ICurrentTenant tenant,
        IUnitOfWork uow,
        IClock clock,
        ICustomerPortalProvisioner provisioner)
        => (_clients, _tenant, _uow, _clock, _provisioner) = (clients, tenant, uow, clock, provisioner);

    public async Task<CrmClientView> HandleAsync(
        ConfigureCrmClientPortalCommand command, CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        if (command.Enabled)
        {
            if (!_tenant.IsResolved)
                throw new InvalidOperationException("Cannot provision a customer portal outside a tenant request context.");

            if (string.IsNullOrWhiteSpace(command.Slug))
                throw new ArgumentException("Customer portal slug is required when enabling a portal.", nameof(command.Slug));

            await _provisioner.ProvisionAsync(
                _tenant.TenantId,
                command.Slug.Trim(),
                command.Domain?.Trim(),
                command.PortalBrandName,
                command.PortalBrandLogoUrl,
                ct);
        }

        client.ConfigurePortal(command.Enabled, command.Slug, command.Domain, _clock.UtcNow);
        await _clients.UpdateAsync(client, ct);
        await _uow.SaveChangesAsync(ct);
        return CrmClientMapper.ToView(client);
    }
}

public sealed class ListCrmClientsHandler
    : IQueryHandler<ListCrmClientsQuery, IReadOnlyList<CrmClientView>>
{
    private readonly ICrmClientRepository _clients;

    public ListCrmClientsHandler(ICrmClientRepository clients) => _clients = clients;

    public async Task<IReadOnlyList<CrmClientView>> HandleAsync(
        ListCrmClientsQuery query,
        CancellationToken ct = default)
        => (await _clients.ListForProjectAsync(query.ProjectId, ct))
            .Select(CrmClientMapper.ToView)
            .ToList();
}

public sealed class RunCrmClientAutomationHandler
    : ICommandHandler<RunCrmClientAutomationCommand, CrmChainRunView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmChainRunRepository _crmRuns;
    private readonly IJobChainRepository _chains;
    private readonly ICommandHandler<RunJobChainCommand, ChainRunView> _chainRunner;
    private readonly IRunArtifactLinkRepository _runArtifacts;
    private readonly ICrmClientArtifactRepository _clientArtifacts;
    private readonly IUnitOfWork _uow;

    public RunCrmClientAutomationHandler(
        ICrmClientRepository clients,
        ICrmChainRunRepository crmRuns,
        IJobChainRepository chains,
        ICommandHandler<RunJobChainCommand, ChainRunView> chainRunner,
        IRunArtifactLinkRepository runArtifacts,
        ICrmClientArtifactRepository clientArtifacts,
        IUnitOfWork uow)
        => (_clients, _crmRuns, _chains, _chainRunner, _runArtifacts, _clientArtifacts, _uow)
            = (clients, crmRuns, chains, chainRunner, runArtifacts, clientArtifacts, uow);

    public async Task<CrmChainRunView> HandleAsync(
        RunCrmClientAutomationCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var chain = await _chains.GetByIdAsync(command.ChainId, ct)
            ?? throw new InvalidOperationException($"Job chain {command.ChainId} not found.");
        if (chain.ProjectId != client.ProjectId)
            throw new InvalidOperationException("The job chain and client must belong to the same project.");

        var payload = JsonSerializer.Serialize(new
        {
            customer = new
            {
                id = client.Id,
                name = client.Name,
                company = client.Company,
                email = client.Email,
                phone = client.Phone,
                lifecycleStage = client.LifecycleStage.ToString(),
                notes = client.Notes,
            },
            lifecycle = client.LifecycleStage.ToString(),
        });

        var result = await _chainRunner.HandleAsync(
            new RunJobChainCommand(chain.Id, payload, CrmClientId: client.Id), ct);
        var link = CrmChainRun.Create(client.ProjectId, client.Id, chain.Id, result.Id,
            client.LifecycleStage, result.StartedAt);
        await _crmRuns.AddAsync(link, ct);

        foreach (var runId in result.Steps.Where(step => step.RunId is not null)
                     .Select(step => step.RunId!.Value).Distinct())
        {
            foreach (var artifact in await _runArtifacts.ListForRunAsync(runId, ct))
            {
                if (await _clientArtifacts.ExistsForSourceAsync(client.Id, artifact.Id, ct)) continue;
                await _clientArtifacts.AddAsync(CrmClientArtifact.CreateFromRunArtifact(
                    client.ProjectId, client.Id, artifact.Id, result.Id, artifact.Title,
                    artifact.Bucket, artifact.ObjectKey, artifact.ContentType,
                    artifact.SizeBytes, artifact.CreatedAt), ct);
            }
        }
        await _uow.SaveChangesAsync(ct);

        return new CrmChainRunView(link.Id, client.Id, chain.Id, result.ChainName, result.Id,
            link.LifecycleStage.ToString(), result.Status, result.StartedAt, result.FinishedAt);
    }
}

public sealed class ListCrmClientChainRunsHandler
    : IQueryHandler<ListCrmClientChainRunsQuery, IReadOnlyList<CrmChainRunView>>
{
    private readonly ICrmChainRunRepository _crmRuns;
    private readonly IChainRunRepository _runs;

    public ListCrmClientChainRunsHandler(
        ICrmChainRunRepository crmRuns,
        IChainRunRepository runs)
        => (_crmRuns, _runs) = (crmRuns, runs);

    public async Task<IReadOnlyList<CrmChainRunView>> HandleAsync(
        ListCrmClientChainRunsQuery query,
        CancellationToken ct = default)
    {
        var links = await _crmRuns.ListForClientAsync(query.ClientId, query.Take, ct);
        var views = new List<CrmChainRunView>(links.Count);
        foreach (var link in links)
        {
            var run = await _runs.GetByIdAsync(link.ChainRunId, ct);
            views.Add(new CrmChainRunView(
                link.Id,
                link.ClientId,
                link.ChainId,
                run?.ChainName ?? "Deleted job chain",
                link.ChainRunId,
                link.LifecycleStage.ToString(),
                run?.Status.ToString() ?? "Unavailable",
                run?.StartedAt ?? link.StartedAt,
                run?.FinishedAt));
        }
        return views;
    }
}

public sealed class AddCrmClientNoteHandler
    : ICommandHandler<AddCrmClientNoteCommand, CrmCommunicationView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmCommunicationRepository _communications;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public AddCrmClientNoteHandler(
        ICrmClientRepository clients,
        ICrmCommunicationRepository communications,
        ICurrentUser currentUser,
        IUnitOfWork uow,
        IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _communications, _currentUser, _uow, _clock, _automations)
            = (clients, communications, currentUser, uow, clock, automations);

    public async Task<CrmCommunicationView> HandleAsync(
        AddCrmClientNoteCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var note = CrmCommunication.CreateNote(
            client.ProjectId, client.Id, command.Body, _currentUser.UserId, _clock.UtcNow);
        await _communications.AddAsync(note, ct);
        if (_automations is not null)
            await _automations.EnqueueAsync(
                client, Domain.ValueObjects.CrmAutomationEventType.NoteAdded, ct);
        await _uow.SaveChangesAsync(ct);
        return CrmCommunicationMapper.ToView(note);
    }
}

public sealed class SendCrmClientMessageHandler
    : ICommandHandler<SendCrmClientMessageCommand, CrmCommunicationView>
{
    private readonly ICrmClientRepository _clients;
    private readonly ICrmCommunicationRepository _communications;
    private readonly IClientCommunicationSender _sender;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly CrmAutomationDispatcher? _automations;

    public SendCrmClientMessageHandler(
        ICrmClientRepository clients,
        ICrmCommunicationRepository communications,
        IClientCommunicationSender sender,
        ICurrentUser currentUser,
        IUnitOfWork uow,
        IClock clock,
        CrmAutomationDispatcher? automations = null)
        => (_clients, _communications, _sender, _currentUser, _uow, _clock, _automations)
            = (clients, communications, sender, currentUser, uow, clock, automations);

    public async Task<CrmCommunicationView> HandleAsync(
        SendCrmClientMessageCommand command,
        CancellationToken ct = default)
    {
        var client = await _clients.GetByIdAsync(command.ClientId, ct)
            ?? throw new InvalidOperationException($"Client {command.ClientId} not found.");
        var recipient = command.Channel switch
        {
            Domain.ValueObjects.CrmCommunicationChannel.Email => client.Email,
            Domain.ValueObjects.CrmCommunicationChannel.Sms => client.Phone,
            _ => throw new ArgumentException("Choose email or SMS.", nameof(command.Channel)),
        };
        var message = CrmCommunication.CreateOutbound(
            client.ProjectId, client.Id, command.Channel, command.Subject, command.Body,
            recipient ?? "", _currentUser.UserId, _clock.UtcNow);
        await _communications.AddAsync(message, ct);
        await _uow.SaveChangesAsync(ct);

        try
        {
            var delivery = command.Channel == Domain.ValueObjects.CrmCommunicationChannel.Email
                ? await _sender.SendEmailAsync(recipient!, client.Name, command.Subject!, command.Body, ct)
                : await _sender.SendSmsAsync(recipient!, command.Body, ct);
            message.MarkSent(delivery.Provider, delivery.ExternalId, _clock.UtcNow);
            if (_automations is not null)
                await _automations.EnqueueAsync(
                    client, Domain.ValueObjects.CrmAutomationEventType.CommunicationSent, ct);
        }
        catch (Exception ex)
        {
            var capabilities = await _sender.GetCapabilitiesAsync(ct);
            var provider = command.Channel == Domain.ValueObjects.CrmCommunicationChannel.Email
                ? capabilities.EmailProvider
                : capabilities.SmsProvider;
            message.MarkFailed(provider, ex.Message);
        }

        await _communications.UpdateAsync(message, ct);
        await _uow.SaveChangesAsync(ct);
        return CrmCommunicationMapper.ToView(message);
    }
}

public sealed class ListCrmClientCommunicationsHandler
    : IQueryHandler<ListCrmClientCommunicationsQuery, IReadOnlyList<CrmCommunicationView>>
{
    private readonly ICrmCommunicationRepository _communications;

    public ListCrmClientCommunicationsHandler(ICrmCommunicationRepository communications)
        => _communications = communications;

    public async Task<IReadOnlyList<CrmCommunicationView>> HandleAsync(
        ListCrmClientCommunicationsQuery query,
        CancellationToken ct = default)
        => (await _communications.ListForClientAsync(query.ClientId, query.Take, ct))
            .Select(CrmCommunicationMapper.ToView)
            .ToList();
}

public sealed class GetCrmCommsCapabilitiesHandler
    : IQueryHandler<GetCrmCommsCapabilitiesQuery, CrmCommsCapabilitiesView>
{
    private readonly IClientCommunicationSender _sender;

    public GetCrmCommsCapabilitiesHandler(IClientCommunicationSender sender) => _sender = sender;

    public async Task<CrmCommsCapabilitiesView> HandleAsync(
        GetCrmCommsCapabilitiesQuery query,
        CancellationToken ct = default)
    {
        var value = await _sender.GetCapabilitiesAsync(ct);
        return new CrmCommsCapabilitiesView(
            value.EmailEnabled, value.SmsEnabled, value.EmailProvider, value.SmsProvider);
    }
}

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

public sealed class RemoveCrmClientArtifactHandler
    : ICommandHandler<RemoveCrmClientArtifactCommand, bool>
{
    private readonly ICrmClientArtifactRepository _artifacts;
    private readonly IObjectStore _store;
    private readonly IUnitOfWork _uow;

    public RemoveCrmClientArtifactHandler(
        ICrmClientArtifactRepository artifacts,
        IObjectStore store,
        IUnitOfWork uow)
        => (_artifacts, _store, _uow) = (artifacts, store, uow);

    public async Task<bool> HandleAsync(
        RemoveCrmClientArtifactCommand command,
        CancellationToken ct = default)
    {
        var value = await _artifacts.GetByIdAsync(command.ArtifactId, ct);
        if (value is null) return false;
        if (value.IsDirectUpload) await _store.DeleteAsync(value.Bucket, value.ObjectKey, ct);
        await _artifacts.RemoveAsync(value.Id, ct);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class ListCrmClientArtifactsHandler
    : IQueryHandler<ListCrmClientArtifactsQuery, IReadOnlyList<CrmClientArtifactView>>
{
    private readonly ICrmClientArtifactRepository _artifacts;

    public ListCrmClientArtifactsHandler(ICrmClientArtifactRepository artifacts)
        => _artifacts = artifacts;

    public async Task<IReadOnlyList<CrmClientArtifactView>> HandleAsync(
        ListCrmClientArtifactsQuery query,
        CancellationToken ct = default)
        => (await _artifacts.ListForClientAsync(query.ClientId, query.Take, ct))
            .Select(CrmClientArtifactMapper.ToView)
            .ToList();
}

internal static class CrmClientMapper
{
    public static CrmClientView ToView(CrmClient client) => new(
        client.Id,
        client.ProjectId,
        client.Name,
        client.Company,
        client.Email,
        client.Phone,
        client.LifecycleStage.ToString(),
        client.Notes,
        client.CustomerPortalEnabled,
        client.CustomerPortalSlug,
        client.CustomerPortalDomain,
        client.CreatedAt,
        client.UpdatedAt);
}

internal static class CrmCommunicationMapper
{
    public static CrmCommunicationView ToView(CrmCommunication communication) => new(
        communication.Id,
        communication.ClientId,
        communication.Channel.ToString(),
        communication.Subject,
        communication.Body,
        communication.Recipient,
        communication.Status.ToString(),
        communication.Provider,
        communication.Error,
        communication.CreatedByUserId,
        communication.CreatedAt,
        communication.SentAt);
}

internal static class CrmClientArtifactMapper
{
    public static CrmClientArtifactView ToView(CrmClientArtifact value) => new(
        value.Id,
        value.ClientId,
        value.Title,
        value.ContentType,
        value.SizeBytes,
        value.IsDirectUpload ? "Upload" : "Automation",
        value.ChainRunId,
        value.CreatedAt);
}

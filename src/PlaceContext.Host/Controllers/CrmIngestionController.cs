using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PlaceContext.Application;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Crm.Infrastructure.Crm;
using PlaceContext.Infrastructure.Persistence;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/crm/ingest")]
[AllowAnonymous]
[EnableRateLimiting("public-ingestion")]
public sealed class CrmIngestionController : ControllerBase
{
    private readonly CrmIngestionSettingsService _settings;
    private readonly ICrmClientRepository _clients;
    private readonly ICommandHandler<SaveCrmClientCommand, CrmClientView> _save;
    private readonly CrmAutomationDispatcher _automations;
    private readonly IProjectDataStore _projectData;
    private readonly AppDbContext _db;
    private readonly IDataEncryptor _encryptor;
    private readonly IPlaceContextService _service;
    private readonly IRunArtifactLinkRepository _artifactLinks;
    private readonly IObjectStore _objectStore;
    private readonly IUnitOfWork _uow;
    private readonly IValidator<JsonElement> _validator;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public CrmIngestionController(
        CrmIngestionSettingsService settings,
        ICrmClientRepository clients,
        ICommandHandler<SaveCrmClientCommand, CrmClientView> save,
        CrmAutomationDispatcher automations,
        IProjectDataStore projectData,
        AppDbContext db,
        IDataEncryptor encryptor,
        IPlaceContextService service,
        IRunArtifactLinkRepository artifactLinks,
        IObjectStore objectStore,
        IUnitOfWork uow,
        IValidator<JsonElement> validator)
        => (_settings, _clients, _save, _automations, _projectData, _db, _encryptor,
                _service, _artifactLinks, _objectStore, _uow, _validator)
            = (settings, clients, save, automations, projectData, db, encryptor,
                service, artifactLinks, objectStore, uow, validator);

    [HttpOptions]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var origin = Request.Headers.Origin.ToString();
        if (!await _settings.IsKnownOriginAsync(origin, ct)) return NoContent();
        ApplyCors(origin);
        return NoContent();
    }

    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Ingest([FromBody] JsonElement payload, CancellationToken ct)
    {
        var token = Request.Headers[CrmIngestionSettingsService.TokenHeader].ToString();
        var resolved = await _settings.ResolveAsync(token, ct);
        if (resolved is null) return Unauthorized(new { error = "Invalid CRM ingestion token." });

        var origin = Request.Headers.Origin.ToString();
        string normalizedOrigin;
        try { normalizedOrigin = CrmIngestionSettingsService.NormalizeOrigin(origin); }
        catch (ArgumentException) { return StatusCode(StatusCodes.Status403Forbidden, new { error = "Origin is required." }); }
        if (!string.Equals(normalizedOrigin, resolved.AllowedOrigin, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Origin is not allowed." });
        ApplyCors(normalizedOrigin);

        var validation = await _validator.ValidateAsync(payload, ct);
        if (!validation.IsValid)
            return BadRequest(new
            {
                error = validation.Errors[0].ErrorMessage,
                errors = validation.Errors.Select(error => error.ErrorMessage).Distinct(),
            });

        LeadIngestionRequest? request = null;
        if (payload.ValueKind == JsonValueKind.Object)
            request = payload.Deserialize<LeadIngestionRequest>(WebJson);

        // Conventional contact-form honeypot: acknowledge bots without adding a CRM record or job.
        if (!string.IsNullOrWhiteSpace(request?.Website)) return Accepted(new { accepted = true });

        var isLead = request is not null
            && !string.IsNullOrWhiteSpace(request.Name)
            && (!string.IsNullOrWhiteSpace(request.Email) || !string.IsNullOrWhiteSpace(request.Phone));

        var previousTenant = CurrentTenant.Current;
        CurrentTenant.Set(resolved.Tenant);
        try
        {
            CrmClientView? result = null;
            CrmClient? existing = null;
            if (isLead)
            {
                existing = await _clients.FindByContactAsync(
                    resolved.ProjectId, request!.Email, request.Phone, ct);
                var submissionNotes = BuildNotes(request);
                var notes = existing is null
                    ? submissionNotes
                    : AppendNotes(existing.Notes, submissionNotes);
                result = await _save.HandleAsync(new SaveCrmClientCommand(
                    resolved.ProjectId,
                    request.Name!.Trim(),
                    Clean(request.Company) ?? existing?.Company,
                    Clean(request.Email) ?? existing?.Email,
                    Clean(request.Phone) ?? existing?.Phone,
                    existing?.LifecycleStage ?? CustomerLifecycleStage.Lead,
                    notes,
                    existing?.Id), ct);
            }

            var queuedSite = CrmSiteQueueSubmission.From(payload, request);
            if (queuedSite is not null)
                await _projectData.InsertRowAsync(
                    resolved.ProjectId, CrmSiteQueueSubmission.TableName, queuedSite.Values, ct);

            var queued = await _automations.EnqueueIngestionAsync(
                resolved.ProjectId, CrmIngestionPayload.JobChainInput(payload), result?.Id, ct);
            await _uow.SaveChangesAsync(ct);

            if (result is null)
                return Accepted(new
                {
                    accepted = true,
                    siteQueued = queuedSite is not null,
                    queuedSiteId = queuedSite?.Id,
                    automationsQueued = queued.Count,
                    automationRuns = queued,
                });
            return StatusCode(existing is null ? StatusCodes.Status201Created : StatusCodes.Status200OK,
                new
                {
                    id = result.Id,
                    status = existing is null ? "created" : "updated",
                    siteQueued = queuedSite is not null,
                    queuedSiteId = queuedSite?.Id,
                    automationsQueued = queued.Count,
                    automationRuns = queued,
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        finally
        {
            if (previousTenant is null) CurrentTenant.Clear();
            else CurrentTenant.Set(previousTenant);
        }
    }

    /// <summary>
    /// Returns the durable lifecycle of one ingestion-triggered automation. The receipt is generic:
    /// callers can track any matching rule and, once allocated, inspect its underlying chain stages.
    /// </summary>
    [HttpGet("runs/{trackingId:guid}")]
    public async Task<IActionResult> GetRun(Guid trackingId, CancellationToken ct)
    {
        var token = Request.Headers[CrmIngestionSettingsService.TokenHeader].ToString();
        var resolved = await _settings.ResolveAsync(token, ct);
        if (resolved is null) return Unauthorized(new { error = "Invalid CRM ingestion token." });

        var origin = Request.Headers.Origin.ToString();
        string normalizedOrigin;
        try { normalizedOrigin = CrmIngestionSettingsService.NormalizeOrigin(origin); }
        catch (ArgumentException) { return StatusCode(StatusCodes.Status403Forbidden, new { error = "Origin is required." }); }
        if (!string.Equals(normalizedOrigin, resolved.AllowedOrigin, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Origin is not allowed." });
        ApplyCors(normalizedOrigin);

        var previousTenant = CurrentTenant.Current;
        CurrentTenant.Set(resolved.Tenant);
        try
        {
            var row = await _db.CrmAutomationQueue.AsNoTracking().FirstOrDefaultAsync(
                value => value.Id == trackingId
                    && value.TenantId == resolved.Tenant.Id
                    && value.ProjectId == resolved.ProjectId, ct);
            if (row is null) return NotFound();

            var chain = row.ChainRunId is { } chainRunId
                ? await _service.GetChainRunAsync(chainRunId, ct)
                : null;
            var error = row.LastError is { Length: > 0 }
                ? _encryptor.Unprotect(row.LastError, DataEncryptionPurpose.CrmAutomation)
                : null;
            var status = row.CompletedAt is not null
                ? chain?.Status ?? row.ResultStatus ?? "Completed"
                : row.FailedAt is not null
                    ? "Failed"
                    : row.ChainRunId is not null || row.ClaimedAt is not null
                        ? "Running"
                        : row.Attempts > 0 ? "Retrying" : "Queued";
            var terminal = row.CompletedAt is not null || row.FailedAt is not null;
            var artifactsByRun = new Dictionary<Guid, IReadOnlyList<RunArtifactLinkView>>();
            if (terminal && chain is not null)
            {
                foreach (var runId in chain.Steps.Select(step => step.RunId).OfType<Guid>().Distinct())
                    artifactsByRun[runId] = await _service.ListRunArtifactsAsync(runId, ct);
            }

            return Ok(new
            {
                trackingId = row.Id,
                row.ProjectId,
                row.RuleId,
                row.RuleName,
                row.ChainId,
                row.ChainRunId,
                status,
                terminal,
                row.Attempts,
                error,
                row.EnqueuedAt,
                startedAt = chain?.StartedAt ?? row.ClaimedAt,
                finishedAt = chain?.FinishedAt ?? row.CompletedAt ?? row.FailedAt,
                steps = chain?.Steps.Select(step => new
                {
                    step.Index,
                    step.StageIndex,
                    step.BranchIndex,
                    step.JobId,
                    step.JobName,
                    step.RunId,
                    step.Status,
                    step.Error,
                    step.StartedAt,
                    step.FinishedAt,
                    artifacts = step.RunId is { } runId
                        ? artifactsByRun.GetValueOrDefault(runId)
                        : null,
                }),
            });
        }
        finally
        {
            if (previousTenant is null) CurrentTenant.Clear();
            else CurrentTenant.Set(previousTenant);
        }
    }

    [HttpGet("runs/{trackingId:guid}/artifacts/{artifactId:guid}")]
    public async Task<IActionResult> GetRunArtifact(
        Guid trackingId, Guid artifactId, CancellationToken ct)
    {
        var token = Request.Headers[CrmIngestionSettingsService.TokenHeader].ToString();
        var resolved = await _settings.ResolveAsync(token, ct);
        if (resolved is null) return Unauthorized(new { error = "Invalid CRM ingestion token." });

        var origin = Request.Headers.Origin.ToString();
        string normalizedOrigin;
        try { normalizedOrigin = CrmIngestionSettingsService.NormalizeOrigin(origin); }
        catch (ArgumentException) { return StatusCode(StatusCodes.Status403Forbidden, new { error = "Origin is required." }); }
        if (!string.Equals(normalizedOrigin, resolved.AllowedOrigin, StringComparison.Ordinal))
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "Origin is not allowed." });
        ApplyCors(normalizedOrigin);

        var previousTenant = CurrentTenant.Current;
        CurrentTenant.Set(resolved.Tenant);
        try
        {
            var row = await _db.CrmAutomationQueue.AsNoTracking().FirstOrDefaultAsync(
                value => value.Id == trackingId
                    && value.TenantId == resolved.Tenant.Id
                    && value.ProjectId == resolved.ProjectId, ct);
            if (row?.ChainRunId is not { } chainRunId) return NotFound();

            var chain = await _service.GetChainRunAsync(chainRunId, ct);
            var allowedRuns = chain?.Steps.Select(step => step.RunId).OfType<Guid>().ToHashSet()
                ?? new HashSet<Guid>();
            var artifact = await _artifactLinks.GetByIdAsync(artifactId, ct);
            if (artifact is null || artifact.ProjectId != resolved.ProjectId ||
                !allowedRuns.Contains(artifact.RunId)) return NotFound();

            var value = await _objectStore.OpenReadAsync(artifact.Bucket, artifact.ObjectKey, ct);
            if (value is null) return NotFound();
            return File(value.Content, artifact.ContentType, artifact.Title);
        }
        finally
        {
            if (previousTenant is null) CurrentTenant.Clear();
            else CurrentTenant.Set(previousTenant);
        }
    }

    private void ApplyCors(string origin)
    {
        Response.Headers.AccessControlAllowOrigin = origin;
        Response.Headers.AccessControlAllowMethods = "GET, POST, OPTIONS";
        Response.Headers.AccessControlAllowHeaders =
            $"Content-Type, {CrmIngestionSettingsService.TokenHeader}";
        Response.Headers.Append("Vary", "Origin");
    }

    private static string? BuildNotes(LeadIngestionRequest request)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Message)) parts.Add(request.Message.Trim());
        if (!string.IsNullOrWhiteSpace(request.Source)) parts.Add($"Source: {request.Source.Trim()}");
        if (request.Metadata is { Count: > 0 })
            parts.Add("Metadata: " + JsonSerializer.Serialize(request.Metadata));
        if (parts.Count == 0) return "Lead received through CRM ingestion endpoint.";
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string? AppendNotes(string? current, string? submission)
    {
        if (string.IsNullOrWhiteSpace(current)) return submission;
        if (string.IsNullOrWhiteSpace(submission)) return current;
        var combined = $"{current.Trim()}\n\n--- Contact form submission ---\n{submission.Trim()}";
        return combined.Length <= 50_000 ? combined : combined[^50_000..];
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}

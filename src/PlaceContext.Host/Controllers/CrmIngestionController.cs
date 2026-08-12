using System.Linq;
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
using PlaceContext.Infrastructure.Crm;
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
            CrmClient? endpointClient = resolved.ClientId == Guid.Empty
                ? null
                : await _clients.GetByIdAsync(resolved.ClientId, ct);
            if (endpointClient is not null && endpointClient.ProjectId != resolved.ProjectId)
                endpointClient = null;
            if (isLead)
            {
                var normalizedEmail = Clean(request!.Email);
                var normalizedPhone = NormalizePhone(Clean(request.Phone));
                var matched = (await _clients.FindByContactMatchesAsync(
                    resolved.ProjectId, normalizedEmail, normalizedPhone, ct)).ToList();
                if (endpointClient is not null && !matched.Any(client => client.Id == endpointClient.Id))
                    matched = matched.Append(endpointClient).ToList();
                existing = endpointClient ?? CanonicalClient(matched, normalizedEmail, normalizedPhone);
                if (endpointClient is not null && existing is null)
                    existing = endpointClient;
                if (existing is not null)
                    await ConsolidateClientProfileClusterAsync(
                        resolved.ProjectId, existing, matched, ct);
                var submissionNotes = BuildNotes(request);
                var groupedNotes = BuildGroupingNotes(matched, existing);
                var combinedSubmissionNotes = groupedNotes is null
                    ? submissionNotes
                    : AppendNotes(submissionNotes, groupedNotes);
                var notes = existing is null
                    ? combinedSubmissionNotes
                    : AppendNotes(existing.Notes, combinedSubmissionNotes);
                result = await _save.HandleAsync(new SaveCrmClientCommand(
                    resolved.ProjectId,
                    request.Name!.Trim(),
                    Clean(request.Company) ?? existing?.Company,
                    normalizedEmail ?? existing?.Email,
                    normalizedPhone ?? existing?.Phone,
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
                ? _encryptor.Unprotect(row.LastError, IDataEncryptor.Purpose.CrmAutomation)
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
        var profilePoint = new Dictionary<string, object?>
        {
            ["source"] = "crm.ingestion",
            ["event"] = "lead.ingested",
            ["receivedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
            ["data"] = new Dictionary<string, object?>
            {
                ["name"] = request.Name?.Trim(),
                ["email"] = Clean(request.Email),
                ["phone"] = NormalizePhone(Clean(request.Phone)),
                ["company"] = Clean(request.Company),
                ["address"] = Clean(request.Address),
                ["website"] = Clean(request.Website),
                ["message"] = Clean(request.Message),
                ["source"] = Clean(request.Source),
                ["metadata"] = request.Metadata,
            },
        };
        return "Contact form data point:" + Environment.NewLine
            + JsonSerializer.Serialize(profilePoint, WebJson);
    }

    private static string? AppendNotes(string? current, string? submission)
    {
        if (string.IsNullOrWhiteSpace(current)) return submission;
        if (string.IsNullOrWhiteSpace(submission)) return current;
        var combined = $"{current.Trim()}\n\n--- Contact form data point ---\n{submission.Trim()}";
        return combined.Length <= 50_000 ? combined : combined[^50_000..];
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var cleaned = new string(phone.Where(ch => char.IsDigit(ch) || ch == '+').ToArray());
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static CrmClient? CanonicalClient(
        IReadOnlyList<CrmClient> matches,
        string? normalizedEmail,
        string? normalizedPhone)
    {
        if (matches.Count == 0) return null;
        if (normalizedEmail is not null)
        {
            var byEmail = matches.FirstOrDefault(client =>
                string.Equals(client.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase));
            if (byEmail is not null) return byEmail;
        }
        if (normalizedPhone is not null)
        {
            var byPhone = matches.FirstOrDefault(client =>
                string.Equals(NormalizePhone(client.Phone), normalizedPhone, StringComparison.Ordinal));
            if (byPhone is not null) return byPhone;
        }
        return matches.OrderBy(client => client.CreatedAt).FirstOrDefault();
    }

    private async Task ConsolidateClientProfileClusterAsync(
        Guid projectId,
        CrmClient canonical,
        IReadOnlyList<CrmClient> matches,
        CancellationToken ct)
    {
        var duplicateIds = matches
            .Where(client => client.Id != canonical.Id)
            .Select(client => client.Id)
            .ToArray();
        if (duplicateIds.Length == 0) return;

        var duplicateSet = duplicateIds.ToHashSet();

        var duplicateCommunications = await _db.CrmCommunications
            .Where(item => item.ProjectId == projectId && duplicateSet.Contains(item.ClientId))
            .ToListAsync(ct);
        foreach (var row in duplicateCommunications)
            row.ClientId = canonical.Id;

        var duplicateArtifacts = await _db.CrmClientArtifacts
            .Where(item => item.ProjectId == projectId && duplicateSet.Contains(item.ClientId))
            .ToListAsync(ct);
        foreach (var row in duplicateArtifacts)
            row.ClientId = canonical.Id;

        var duplicateChainRuns = await _db.CrmChainRuns
            .Where(item => item.ProjectId == projectId && duplicateSet.Contains(item.ClientId))
            .ToListAsync(ct);
        foreach (var row in duplicateChainRuns)
            row.ClientId = canonical.Id;

        var duplicateJobRuns = await _db.CrmJobRuns
            .Where(item => item.ProjectId == projectId && duplicateSet.Contains(item.ClientId))
            .ToListAsync(ct);
        foreach (var row in duplicateJobRuns)
            row.ClientId = canonical.Id;

        var canonicalChainIds = (await _db.CrmClientJobChainAssignments
            .Where(item => item.ProjectId == projectId && item.ClientId == canonical.Id)
            .Select(item => item.ChainId)
            .ToListAsync(ct))
            .ToHashSet();
        var duplicateAssignments = await _db.CrmClientJobChainAssignments
            .Where(item => item.ProjectId == projectId && duplicateSet.Contains(item.ClientId))
            .ToListAsync(ct);
        foreach (var row in duplicateAssignments)
        {
            if (canonicalChainIds.Contains(row.ChainId))
            {
                _db.CrmClientJobChainAssignments.Remove(row);
                continue;
            }

            row.ClientId = canonical.Id;
            canonicalChainIds.Add(row.ChainId);
        }

        var duplicateAppointments = await _db.CrmAppointments
            .Where(item => item.ProjectId == projectId && item.ClientId.HasValue
                && duplicateSet.Contains(item.ClientId.Value))
            .ToListAsync(ct);
        foreach (var row in duplicateAppointments)
            row.ClientId = canonical.Id;

        var duplicateQueueEntries = await _db.CrmAutomationQueue
            .Where(item => item.ProjectId == projectId && item.ClientId.HasValue
                && duplicateSet.Contains(item.ClientId.Value))
            .ToListAsync(ct);
        foreach (var row in duplicateQueueEntries)
            row.ClientId = canonical.Id;

        var duplicateClients = await _db.CrmClients
            .Where(item => item.ProjectId == projectId && duplicateSet.Contains(item.Id))
            .ToListAsync(ct);
        _db.CrmClients.RemoveRange(duplicateClients);
    }

    private static string? BuildGroupingNotes(
        IReadOnlyList<CrmClient> matches,
        CrmClient? canonical)
    {
        if (canonical is null) return null;
        var groupedIds = matches.Where(client => client.Id != canonical.Id)
            .Select(client => client.Id)
            .ToList();
        if (groupedIds.Count == 0) return null;

        return $"Grouped profile cluster ({groupedIds.Count} other profile(s) merged for lead correlation): " +
               string.Join(", ", groupedIds);
    }

}

public sealed record LeadIngestionRequest(
    string? Name,
    string? Email,
    string? Phone,
    string? Company,
    string? Message,
    string? Source,
    string? Address,
    Dictionary<string, JsonElement>? Metadata,
    string? Website);

/// <summary>
/// Produces the payload consumed by ingestion-triggered job chains. Report clients historically
/// sent the report-order contract inside a contact-form metadata property; the report chain uses
/// that contract directly. All other ingestion payloads remain opaque and are forwarded unchanged.
/// </summary>
public static class CrmIngestionPayload
{
    public static string JobChainInput(JsonElement payload)
    {
        if (IsReportOrder(payload)) return payload.GetRawText();

        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("metadata", out var metadata)
            && IsReportOrder(metadata))
            return metadata.GetRawText();

        return payload.GetRawText();
    }

    private static bool IsReportOrder(JsonElement payload)
        => payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("event", out var eventName)
            && eventName.ValueKind == JsonValueKind.String
            && string.Equals(eventName.GetString(), "feasibility_report_ordered", StringComparison.Ordinal)
            && payload.TryGetProperty("site", out var site)
            && site.ValueKind == JsonValueKind.Object;
}

/// <summary>
/// Converts an address-bearing CRM submission into the existing Ossen project queue contract.
/// The metadata fallback accepts older report clients that only supplied metadata.site.address.
/// </summary>
public sealed record CrmSiteQueueSubmission(Guid Id, IReadOnlyDictionary<string, string?> Values)
{
    public const string TableName = "queue_sites";

    public static CrmSiteQueueSubmission? From(LeadIngestionRequest? request)
    {
        var address = Clean(request?.Address) ?? MetadataAddress(request?.Metadata);
        return FromAddress(address);
    }

    public static CrmSiteQueueSubmission? From(JsonElement payload, LeadIngestionRequest? request = null)
    {
        var address = Clean(request?.Address)
            ?? SiteAddress(payload)
            ?? MetadataAddress(request?.Metadata);
        return FromAddress(address);
    }

    private static CrmSiteQueueSubmission? FromAddress(string? address)
    {
        if (address is null) return null;

        var id = Guid.NewGuid();
        return new CrmSiteQueueSubmission(id, new Dictionary<string, string?>
        {
            ["id"] = id.ToString(),
            ["address"] = address,
            ["status"] = "NOT_RUN",
            ["error"] = null,
            ["retry_attempt"] = "0",
            ["last_run_at"] = null,
        });
    }

    private static string? SiteAddress(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("site", out var site)
            || site.ValueKind != JsonValueKind.Object
            || !site.TryGetProperty("address", out var address)
            || address.ValueKind != JsonValueKind.String)
            return null;
        return Clean(address.GetString());
    }

    private static string? MetadataAddress(Dictionary<string, JsonElement>? metadata)
    {
        if (metadata is null
            || !metadata.TryGetValue("site", out var site)
            || site.ValueKind != JsonValueKind.Object
            || !site.TryGetProperty("address", out var address)
            || address.ValueKind != JsonValueKind.String)
            return null;
        return Clean(address.GetString());
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

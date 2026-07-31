using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Features;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;
using PlaceContext.Infrastructure.Crm;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Host.Controllers;

[ApiController]
[Route("api/crm/ingest")]
[AllowAnonymous]
public sealed class CrmIngestionController : ControllerBase
{
    private readonly CrmIngestionSettingsService _settings;
    private readonly ICrmClientRepository _clients;
    private readonly ICommandHandler<SaveCrmClientCommand, CrmClientView> _save;

    public CrmIngestionController(
        CrmIngestionSettingsService settings,
        ICrmClientRepository clients,
        ICommandHandler<SaveCrmClientCommand, CrmClientView> save)
        => (_settings, _clients, _save) = (settings, clients, save);

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
    public async Task<IActionResult> Ingest([FromBody] LeadIngestionRequest request, CancellationToken ct)
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

        // Conventional contact-form honeypot: acknowledge bots without adding a CRM record.
        if (!string.IsNullOrWhiteSpace(request.Website)) return Accepted(new { accepted = true });
        var validationError = Validate(request);
        if (validationError is not null) return BadRequest(new { error = validationError });

        var previousTenant = CurrentTenant.Current;
        CurrentTenant.Set(resolved.Tenant);
        try
        {
            var existing = await _clients.FindByContactAsync(
                resolved.ProjectId, request.Email, request.Phone, ct);
            var submissionNotes = BuildNotes(request);
            var notes = existing is null
                ? submissionNotes
                : AppendNotes(existing.Notes, submissionNotes);
            var result = await _save.HandleAsync(new SaveCrmClientCommand(
                resolved.ProjectId,
                request.Name.Trim(),
                Clean(request.Company) ?? existing?.Company,
                Clean(request.Email) ?? existing?.Email,
                Clean(request.Phone) ?? existing?.Phone,
                existing?.LifecycleStage ?? CustomerLifecycleStage.Lead,
                notes,
                existing?.Id), ct);
            return StatusCode(existing is null ? StatusCodes.Status201Created : StatusCodes.Status200OK,
                new { id = result.Id, status = existing is null ? "created" : "updated" });
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

    private void ApplyCors(string origin)
    {
        Response.Headers.AccessControlAllowOrigin = origin;
        Response.Headers.AccessControlAllowMethods = "POST, OPTIONS";
        Response.Headers.AccessControlAllowHeaders =
            $"Content-Type, {CrmIngestionSettingsService.TokenHeader}";
        Response.Headers.Append("Vary", "Origin");
    }

    private static string? Validate(LeadIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Name is required.";
        if (request.Name.Trim().Length > 200) return "Name is too long.";
        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Phone))
            return "Email or phone is required.";
        if (request.Email?.Length > 320 || request.Email is not null && !request.Email.Contains('@'))
            return "Enter a valid email address.";
        if (request.Phone?.Length > 80) return "Phone is too long.";
        if (request.Company?.Length > 300) return "Company is too long.";
        if (request.Message?.Length > 10_000) return "Message is too long.";
        if (request.Source?.Length > 200) return "Source is too long.";
        if (request.Metadata is { Count: > 30 }) return "Metadata has too many fields.";
        return null;
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

    public sealed record LeadIngestionRequest(
        string Name,
        string? Email,
        string? Phone,
        string? Company,
        string? Message,
        string? Source,
        Dictionary<string, JsonElement>? Metadata,
        string? Website);
}

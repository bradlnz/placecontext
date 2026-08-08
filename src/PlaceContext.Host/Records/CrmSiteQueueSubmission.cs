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

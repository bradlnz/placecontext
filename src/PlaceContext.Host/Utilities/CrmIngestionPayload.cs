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

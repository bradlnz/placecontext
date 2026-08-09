using System.Text.Json;

namespace PlaceContext.Crm.Contracts.Ingestion;

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

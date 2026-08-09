using System.Text.Json;
using PlaceContext.Crm.Contracts.Ingestion;

namespace PlaceContext.Crm.Ingestion;

public sealed record CrmSiteQueueSubmission(Guid Id, IReadOnlyDictionary<string, string?> Values)
{
    public const string TableName = "queue_sites";

    public static CrmSiteQueueSubmission? From(JsonElement payload, LeadIngestionRequest? request = null)
    {
        var address = Clean(request?.Address)
            ?? SiteAddress(payload)
            ?? MetadataAddress(request?.Metadata);
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

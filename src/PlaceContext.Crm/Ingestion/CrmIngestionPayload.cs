using System.Text.Json;

namespace PlaceContext.Crm.Ingestion;

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

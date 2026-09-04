using Microsoft.Extensions.Logging;
using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Features;

/// <summary>The write-path hook every record mutation shares: refresh the table's link slice,
/// best-effort — a failure is logged and never changes the outcome of the write.</summary>
internal static class RecordLinkHook
{
    public static async Task RefreshAsync(RecordLinkService? links, Guid projectId, string table,
        ILogger? log, CancellationToken ct)
    {
        if (links is null) return;
        try
        {
            await links.RefreshTableAsync(projectId, table, ct);
        }
        catch (Exception ex)
        {
            log?.LogWarning(ex, "Record-link refresh of '{Table}' failed — the write is unaffected.", table);
        }
    }
}

using Microsoft.JSInterop;
using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Application.Shared;

namespace PlaceContext.Host.Components.ViewModels;

public sealed partial class DataMapViewModel
{
    // ── Data helpers ──────────────────────────────────────────────────────────────────────────
    public string ReturnTypeOf(Guid jobId) =>
        Jobs?.FirstOrDefault(j => j.Id == jobId)?.ReturnType.ToString() ?? "?";

    public long? TableRows(string table) =>
        Tables
            ?.FirstOrDefault(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase))
            ?.RowEstimate;

    public IReadOnlyList<JobView> UnmappedJobs() =>
        (Jobs ?? Array.Empty<JobView>())
            .Where(j => Mappings?.All(m => m.JobId != j.Id) ?? true)
            .ToList();

    public IReadOnlyList<string> TableNodes()
    {
        var mapped = (Mappings ?? Array.Empty<DataMappingView>()).Select(m => m.TargetTable);
        var real = (Tables ?? Array.Empty<ProjectTableInfo>())
            .Where(t => !t.IsView)
            .Select(t => t.Name);
        return mapped.Concat(real).Distinct(StringComparer.OrdinalIgnoreCase).Take(14).ToList();
    }

    public (double X, double Y) GetPos(string key) =>
        Pos.TryGetValue(key, out var p) ? p : (24, 24);

    // ── Utilities ─────────────────────────────────────────────────────────────────────────────
    public static string InferType(System.Text.Json.JsonElement v) =>
        v.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => v.TryGetInt64(out _)
                ? DataColumnTypes.Bigint
                : DataColumnTypes.Numeric,
            System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False =>
                DataColumnTypes.Boolean,
            System.Text.Json.JsonValueKind.Object or System.Text.Json.JsonValueKind.Array =>
                DataColumnTypes.Jsonb,
            System.Text.Json.JsonValueKind.String
                when DateTimeOffset.TryParse(v.GetString(), out _) => DataColumnTypes.Timestamptz,
            _ => DataColumnTypes.Text,
        };

    public static string Sanitize(string name)
    {
        var s = new string(
            name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()
        ).Trim('_');
        if (s.Length == 0)
            s = "col";
        if (char.IsDigit(s[0]))
            s = "_" + s;
        return s.Length > 63 ? s[..63] : s;
    }
}

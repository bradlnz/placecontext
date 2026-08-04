using PlaceContext.Application.Dtos;

namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Shared formatting utilities used across multiple pages.
/// Eliminates the FormatBytes / FormatDuration / FormatMs / PrettyJson / DataUri
/// duplication across Jobs, JobChains, Dashboard, Observability, Chat, and Artifacts.
/// </summary>
public static class FormatHelper
{
    public static string Bytes(long n) =>
        n >= 1_048_576 ? $"{n / 1_048_576.0:0.#} MB"
        : n >= 1024 ? $"{n / 1024.0:0.#} KB"
        : $"{n} B";

    public static string Duration(DateTimeOffset start, DateTimeOffset end)
    {
        var span = end - start;
        if (span.TotalSeconds < 60)
            return $"{(int)span.TotalSeconds}s";
        return $"{(int)span.TotalMinutes}m {span.Seconds}s";
    }

    public static string Duration(JobRunDetailView run) =>
        run.FinishedAt is { } f
            ? (f - run.StartedAt) is { TotalSeconds: < 60 } s
                ? $"{(int)s.TotalSeconds}s"
                : $"{(int)(f - run.StartedAt).TotalMinutes}m {(f - run.StartedAt).Seconds}s"
            : "—";

    public static string Ms(double? ms) =>
        ms switch
        {
            null => "—",
            < 1000 => $"{ms:0}ms",
            < 60000 => $"{ms / 1000:0.#}s",
            _ => $"{ms / 60000:0.#}m",
        };

    public static string PrettyJson(string raw)
    {
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(
                doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );
        }
        catch
        {
            return raw;
        }
    }

    public static string DataUri(RunArtifactView a)
    {
        var contentType =
            a.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "text/csv"
            : a.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json"
            : a.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? "text/html"
            : a.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml"
            : a.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "application/pdf"
            : a.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
            : a.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || a.Name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
            : "text/plain";
        var b64 = a.IsBinary
            ? a.Content
            : Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(a.Content));
        return $"data:{contentType};base64,{b64}";
    }

    public static string Trunc(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "—"
        : s.Length <= n ? s
        : s[..n] + "…";

    public static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&#39;");
}

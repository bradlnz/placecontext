namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Shared CSS-variable helpers for run/job status badges.
/// Eliminates the StatusColor / StatusBg duplication across Jobs, JobChains,
/// Dashboard, Observability, JobEditor, and Inspector pages.
/// </summary>
public static class StatusHelper
{
    public static string Color(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "succeeded" => "var(--success)",
            "partial" => "var(--warn)",
            "failed" => "var(--bad)",
            "running" => "var(--running)",
            _ => "var(--text-3)",
        };

    public static string Background(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "succeeded" => "var(--success-bg)",
            "partial" => "var(--warn-bg)",
            "failed" => "var(--bad-bg)",
            "running" => "var(--running-bg)",
            _ => "var(--card-2)",
        };
}

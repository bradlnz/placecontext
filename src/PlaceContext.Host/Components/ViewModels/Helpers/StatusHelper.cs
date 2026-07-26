namespace PlaceContext.Host.Components.ViewModels.Helpers;

/// <summary>
/// Shared CSS-variable helpers for run/job status badges.
/// Eliminates the StatusColor / StatusBg duplication across Jobs, JobChains,
/// Dashboard, Observability, JobEditor, and Inspector pages.
/// </summary>
public static class StatusHelper
{
    public static string Color(string? status) => status switch
    {
        "Succeeded" => "var(--good)",
        "Partial" => "var(--warn)",
        "Failed" => "var(--bad)",
        "Running" => "var(--brand-2)",
        _ => "var(--text-3)"
    };

    public static string Background(string? status) => status switch
    {
        "Succeeded" => "var(--good-bg)",
        "Partial" => "var(--warn-bg)",
        "Failed" => "var(--bad-bg)",
        "Running" => "var(--brand-bg)",
        _ => "var(--card-2)"
    };
}

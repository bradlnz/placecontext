namespace PlaceContext.Host.Components.ViewModels;

public static class JobEditorStatusCatalog
{
    public const string Succeeded = "Succeeded";
    public const string Partial = "Partial";
    public const string Failed = "Failed";
    public const string Running = "Running";

    public static string Color(string status) =>
        status switch
        {
            Succeeded => "var(--good)",
            Partial => "var(--warn)",
            Failed => "var(--bad)",
            Running => "var(--brand-2)",
            _ => "var(--text-3)",
        };

    public static string Background(string status) =>
        status switch
        {
            Succeeded => "var(--good-bg)",
            Partial => "var(--warn-bg)",
            Failed => "var(--bad-bg)",
            Running => "var(--brand-bg)",
            _ => "var(--card-2)",
        };
}

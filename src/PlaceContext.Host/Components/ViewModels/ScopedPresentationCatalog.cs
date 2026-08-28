using PlaceContext.Host.Components.ViewModels.Helpers;

namespace PlaceContext.Host.Components.ViewModels;

public enum JobRunStatus
{
    Unknown,
    Queued,
    Running,
    Succeeded,
    Failed,
    Partial,
}

public enum ChainStepStatus
{
    Unknown,
    Pending,
    Running,
    Succeeded,
    Failed,
    Partial,
    Skipped,
}

public enum MapSourceMode
{
    Image,
    Code,
}

public static class ScopedPresentationCatalog
{
    public static JobRunStatus JobStatus(string? value) => Parse(value, JobRunStatus.Unknown);

    public static ChainStepStatus StepStatus(string? value) =>
        Parse(value, ChainStepStatus.Unknown);

    public static PlaceContext.Domain.ValueObjects.TriggerKind Trigger(string? value) =>
        Parse(value, PlaceContext.Domain.ValueObjects.TriggerKind.Schedule);

    public static bool IsRunning(string? value) =>
        string.Equals(value, "Running", StringComparison.OrdinalIgnoreCase);

    public static bool IsQueuedOrRunning(string? value) =>
        value is not null
        && (value.Equals("Queued", StringComparison.OrdinalIgnoreCase) || IsRunning(value));

    public static string Color(string? value) => StatusHelper.Color(value);

    public static string Background(string? value) => StatusHelper.Background(value);

    private static T Parse<T>(string? value, T fallback)
        where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
}

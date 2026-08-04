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

public enum ChainRunStatus
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

public enum EditorTab
{
    Details,
    Runs,
    Triggers,
}

public enum ChainEditorView
{
    Canvas,
    List,
}

public enum ChainGateEditorType
{
    Wait,
    Condition,
}

public enum GateOperator
{
    Exists,
    NotExists,
    Empty,
    NotEmpty,
    Equals,
    NotEquals,
    In,
    NotIn,
}

public enum MapSourceMode
{
    Image,
    Code,
}

public enum PayloadMode
{
    Raw,
    Form,
}

public static class ScopedPresentationCatalog
{
    public static JobRunStatus JobStatus(string? value) => Parse(value, JobRunStatus.Unknown);

    public static ChainRunStatus ChainStatus(string? value) => Parse(value, ChainRunStatus.Unknown);

    public static ChainStepStatus StepStatus(string? value) =>
        Parse(value, ChainStepStatus.Unknown);

    public static PlaceContext.Domain.ValueObjects.TriggerKind Trigger(string? value) =>
        Parse(value, PlaceContext.Domain.ValueObjects.TriggerKind.Schedule);

    public static bool IsRunning(string? value) =>
        JobStatus(value) == JobRunStatus.Running
        || ChainStatus(value) == ChainRunStatus.Running
        || StepStatus(value) == ChainStepStatus.Running;

    public static bool IsQueuedOrRunning(string? value) =>
        value is not null
        && (value.Equals("Queued", StringComparison.OrdinalIgnoreCase) || IsRunning(value));

    public static string Color(string? value) => StatusColor(JobStatus(value));

    public static string Background(string? value) => StatusBackground(JobStatus(value));

    public static string StatusColor(JobRunStatus status) =>
        status switch
        {
            JobRunStatus.Succeeded => "var(--good)",
            JobRunStatus.Failed => "var(--bad)",
            JobRunStatus.Partial => "var(--warn)",
            JobRunStatus.Running => "var(--brand-2)",
            _ => "var(--text-3)",
        };

    public static string StatusColor(ChainRunStatus status) =>
        status switch
        {
            ChainRunStatus.Succeeded => "var(--good)",
            ChainRunStatus.Failed => "var(--bad)",
            ChainRunStatus.Partial => "var(--warn)",
            ChainRunStatus.Running => "var(--brand-2)",
            _ => "var(--text-3)",
        };

    public static string StatusColor(ChainStepStatus status) =>
        status switch
        {
            ChainStepStatus.Succeeded => "var(--good)",
            ChainStepStatus.Failed => "var(--bad)",
            ChainStepStatus.Partial => "var(--warn)",
            ChainStepStatus.Running => "var(--brand-2)",
            _ => "var(--text-3)",
        };

    public static string StatusBackground(JobRunStatus status) =>
        status switch
        {
            JobRunStatus.Succeeded => "var(--good-bg)",
            JobRunStatus.Failed => "var(--bad-bg)",
            JobRunStatus.Partial => "var(--warn-bg)",
            JobRunStatus.Running => "var(--brand-bg)",
            _ => "var(--card-2)",
        };

    public static string StatusBackground(ChainRunStatus status) =>
        status switch
        {
            ChainRunStatus.Succeeded => "var(--good-bg)",
            ChainRunStatus.Failed => "var(--bad-bg)",
            ChainRunStatus.Partial => "var(--warn-bg)",
            ChainRunStatus.Running => "var(--brand-bg)",
            _ => "var(--card-2)",
        };

    public static string StatusBackground(ChainStepStatus status) =>
        status switch
        {
            ChainStepStatus.Succeeded => "var(--good-bg)",
            ChainStepStatus.Failed => "var(--bad-bg)",
            ChainStepStatus.Partial => "var(--warn-bg)",
            ChainStepStatus.Running => "var(--brand-bg)",
            _ => "var(--card-2)",
        };

    private static T Parse<T>(string? value, T fallback)
        where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
}

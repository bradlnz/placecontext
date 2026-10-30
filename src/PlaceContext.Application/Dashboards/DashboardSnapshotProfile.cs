namespace PlaceContext.Application.Dashboards;

public sealed record DashboardSnapshotProfile(
    IReadOnlyList<DashboardDataProfile> Charts,
    DashboardConfidence Confidence,
    string ConfidenceReason,
    int NeedsAttentionCount,
    int CurrentCount,
    DateTimeOffset? OldestGeneratedAt)
{
    public static DashboardSnapshotProfile Create(DashboardSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var profiles = snapshot.Charts.Select(x => DashboardDataProfile.Create(x, now)).ToArray();
        var confidence = profiles.Length == 0 || profiles.Any(x => x.Confidence == DashboardConfidence.Unknown)
            ? DashboardConfidence.Unknown
            : profiles.Any(x => x.Confidence == DashboardConfidence.Low) ? DashboardConfidence.Low
            : DashboardConfidence.Medium;
        var reason = confidence switch
        {
            DashboardConfidence.Unknown => profiles.Length == 0 ? "Choose a source chart to inspect its data profile."
                : "At least one selected chart has no usable evidence; a combined assessment is incomplete.",
            DashboardConfidence.Low => "At least one selected chart needs attention. Inspect its evidence checks before using this dashboard to make a decision.",
            _ => "Selected snapshots are usable, but their source coverage has not been verified."
        };
        var knownDates = profiles.Where(x => x.Age is not null).Select(x => x.GeneratedAt).ToArray();
        return new(profiles, confidence, reason, profiles.Count(x => x.NeedsAttention),
            profiles.Count(x => x.Freshness == DashboardFreshness.Current),
            knownDates.Length == 0 ? null : knownDates.Min());
    }
}

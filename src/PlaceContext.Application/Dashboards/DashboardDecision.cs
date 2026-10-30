namespace PlaceContext.Application.Dashboards;

public enum DashboardDecisionStatus { Review, Watch, Hold, NeedsData, Unavailable }

/// <summary>Jev support for a small local review decision, independent of evidence confidence.</summary>
public sealed record DashboardDecision(string SignalId, DashboardDecisionStatus Status, double? Probability,
    string Rationale, string NextAction, string Question, string? Model,
    DateTimeOffset EvaluatedAt, string Fingerprint)
{
    public const double ReviewSupport = .9;
    public const double WatchSupport = .1;

    public static DashboardDecision FromJudgment(DashboardSignal signal, double probability, string? model,
        DateTimeOffset at, string fingerprint)
    {
        if (signal.NeedsData) return ForMissingData(signal, at, fingerprint);
        if (!double.IsFinite(probability) || probability is < 0 or > 1) return Failed(signal, at, fingerprint);
        var contradiction = signal.TargetBreached is { } breached
            && (breached && probability <= WatchSupport || !breached && probability >= ReviewSupport);
        var status = contradiction ? DashboardDecisionStatus.Hold
            : probability >= ReviewSupport ? DashboardDecisionStatus.Review
            : probability <= WatchSupport ? DashboardDecisionStatus.Watch : DashboardDecisionStatus.Hold;
        var explanation = contradiction ? "Jev's judgment conflicts with the measured target comparison; the decision is held for review."
            : status == DashboardDecisionStatus.Review ? "Jev supports prioritizing a review of these displayed values."
            : status == DashboardDecisionStatus.Watch ? "Jev did not support prioritizing this review; this does not establish business health or source completeness."
            : "Jev's support falls between the review and watch boundaries; inspect the evidence or refine the target.";
        var next = status == DashboardDecisionStatus.Review ? "Inspect the displayed values"
            : status == DashboardDecisionStatus.Watch ? "Recheck when the snapshot or target changes"
            : "Review the measured comparison and source";
        return new(signal.Id, status, probability, signal.Rationale + " " + explanation, next,
            signal.Question, model, at, fingerprint);
    }

    public static DashboardDecision ForMissingData(DashboardSignal signal, DateTimeOffset at, string fingerprint)
        => new(signal.Id, DashboardDecisionStatus.NeedsData, null, signal.Rationale,
            "Refresh or repair the source in Analytics", signal.Question, null, at, fingerprint);

    public static DashboardDecision Failed(DashboardSignal signal, DateTimeOffset at, string fingerprint)
        => new(signal.Id, DashboardDecisionStatus.Unavailable, null,
            signal.Rationale + " Jev did not return a usable judgment. No recommendation has been made.",
            "Retry Jev review", signal.Question, null, at, fingerprint);
}

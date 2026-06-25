using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain service that scores the *process-trust* risk of a single change. Process risk is the
/// risk that accrues when an agent-authored change skips the trust gates Brad's standards require:
/// tests, recorded rationale, architecture review, and live verification — plus churning a coupling
/// hotspot or re-touching the same nodes within a short window. No I/O: callers supply the god-node
/// set and the re-touch flag (computed from the ledger by the caller).
/// </summary>
public sealed class ProcessRiskScorer
{
    public IReadOnlyList<RiskSignal> Score(
        ActivityRecord change,
        IReadOnlyCollection<GodNode> godNodes,
        bool reTouchedWithinWindow)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(godNodes);

        var signals = new List<RiskSignal>();

        if (change.IsAgentAuthored && !change.TestDelta.HasTestActivity)
            signals.Add(RiskSignal.Of("AGENT_NO_TESTS", RiskKind.Process, Severity.High,
                "Agent-authored change added no tests."));

        if (change.IsAgentAuthored && !change.Rationale.IsPresent)
            signals.Add(RiskSignal.Of("AGENT_NO_RATIONALE", RiskKind.Process, Severity.Medium,
                "Agent-authored change recorded no rationale."));

        if (!change.Verification.ArchitectureReviewerRun)
            signals.Add(RiskSignal.Of("NO_ARCH_REVIEW", RiskKind.Process, Severity.Medium,
                "architecture-reviewer was not run on this change."));

        if (!change.Verification.LiveVerified)
            signals.Add(RiskSignal.Of("NOT_LIVE_VERIFIED", RiskKind.Process, Severity.Medium,
                "Change was not live-verified."));

        var godIds = godNodes.Select(g => g.Id).ToHashSet();
        var touchedGods = change.TouchedNodes.Where(godIds.Contains).ToList();
        if (touchedGods.Count > 0)
            signals.Add(RiskSignal.Of("TOUCHES_GOD_NODE", RiskKind.Process, Severity.High,
                $"Change touches {touchedGods.Count} highly-coupled god node(s)."));

        if (reTouchedWithinWindow)
            signals.Add(RiskSignal.Of("RE_TOUCHED", RiskKind.Process, Severity.High,
                "Same nodes were churned again within the recent change window."));

        return signals;
    }
}

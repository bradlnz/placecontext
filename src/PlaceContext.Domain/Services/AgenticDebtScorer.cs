using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain service that scores the *process-trust* debt of a single change. Agentic debt is the
/// debt that accrues when an agent-authored change skips the trust gates Brad's standards require:
/// tests, recorded rationale, architecture review, and live verification — plus churning a coupling
/// hotspot or re-touching the same nodes within a short window. No I/O: callers supply the god-node
/// set and the re-touch flag (computed from the ledger by the caller).
/// </summary>
public sealed class AgenticDebtScorer
{
    public IReadOnlyList<DebtSignal> Score(
        ChangeRecord change,
        IReadOnlyCollection<GodNode> godNodes,
        bool reTouchedWithinWindow)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(godNodes);

        var signals = new List<DebtSignal>();

        if (change.IsAgentAuthored && !change.TestDelta.HasTestActivity)
            signals.Add(DebtSignal.Of("AGENT_NO_TESTS", DebtKind.Agentic, Severity.High,
                "Agent-authored change added no tests."));

        if (change.IsAgentAuthored && !change.Rationale.IsPresent)
            signals.Add(DebtSignal.Of("AGENT_NO_RATIONALE", DebtKind.Agentic, Severity.Medium,
                "Agent-authored change recorded no rationale."));

        if (!change.Verification.ArchitectureReviewerRun)
            signals.Add(DebtSignal.Of("NO_ARCH_REVIEW", DebtKind.Agentic, Severity.Medium,
                "architecture-reviewer was not run on this change."));

        if (!change.Verification.LiveVerified)
            signals.Add(DebtSignal.Of("NOT_LIVE_VERIFIED", DebtKind.Agentic, Severity.Medium,
                "Change was not live-verified."));

        var godIds = godNodes.Select(g => g.Id).ToHashSet();
        var touchedGods = change.TouchedNodes.Where(godIds.Contains).ToList();
        if (touchedGods.Count > 0)
            signals.Add(DebtSignal.Of("TOUCHES_GOD_NODE", DebtKind.Agentic, Severity.High,
                $"Change touches {touchedGods.Count} highly-coupled god node(s)."));

        if (reTouchedWithinWindow)
            signals.Add(DebtSignal.Of("RE_TOUCHED", DebtKind.Agentic, Severity.High,
                "Same nodes were churned again within the recent change window."));

        return signals;
    }
}

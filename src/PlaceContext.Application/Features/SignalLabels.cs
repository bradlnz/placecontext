using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.Services;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Features;

/// <summary>Friendly labels and tones for debt signal codes, shared by the Debt and Ledger views.</summary>
internal static class SignalLabels
{
    public static string Label(string code) => code switch
    {
        "AGENT_NO_TESTS" => "No test delta",
        "AGENT_NO_RATIONALE" => "No rationale recorded",
        "NO_ARCH_REVIEW" => "Architecture-reviewer not run",
        "NOT_LIVE_VERIFIED" => "Not live-verified",
        "TOUCHES_GOD_NODE" => "Touches a god-node",
        "RE_TOUCHED" => "Reverted/re-touched within window",
        "TODO_DENSITY" => "TODO / FIXME markers",
        "HIGH_COMPLEXITY" => "High cyclomatic complexity",
        "LOW_COVERAGE" => "Low test coverage",
        "GOD_NODES" => "God-nodes",
        "WEAK_COUPLING_SIGNAL" => "Weak/ambiguous coupling",
        _ => code
    };

    public static string Tone(Severity s) => s switch
    {
        Severity.Critical or Severity.High => "bad",
        Severity.Medium => "warn",
        _ => "good"
    };
}

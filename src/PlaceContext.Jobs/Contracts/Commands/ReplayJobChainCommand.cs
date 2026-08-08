using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Replay a failed/partial chain run from a specific step. The chain re-executes from
/// <paramref name="FromStepIndex"/> onward, using the previous run's output as input payload
/// for that step. Steps before <paramref name="FromStepIndex"/> are not re-run.
///
/// Use cases:
///   - Fix a failing job and retry the chain from the point of failure
///   - Re-run a subset of a long chain after changing upstream data
///   - Debug by replaying a specific step with different parameters
/// </summary>
/// <param name="ChainId">The chain definition to replay.</param>
/// <param name="OriginalRunId">The previous chain run to replay from.</param>
/// <param name="FromStepIndex">0-based flat step index to resume from (default: first failed step).</param>
/// <param name="InputPayload">Optional override for the input payload at the replay start step.</param>
/// <param name="StepPayloadOverrides">Optional per-step parameter overrides (same format as RunJobChainCommand).</param>
public sealed record ReplayJobChainCommand(
    Guid ChainId,
    Guid OriginalRunId,
    int? FromStepIndex = null,
    string? InputPayload = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null)
    : ICommand<ChainRunView>;

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Run a chain now: each step's primary output (reduce artifact if present, else the shard artifacts)
/// becomes the next step's input payload. <paramref name="InputPayload"/> feeds the FIRST step; when
/// null the first job runs with its stored shard payloads. <paramref name="ChainRunId"/> optionally
/// pre-allocates the run's id so the caller can correlate its own tracking with the run row.
/// </summary>
/// <param name="StepPayloadOverrides">Optional JSON-object payloads keyed by step index, collected
/// from each step job's declared parameters. Merged over the step's chained input: object-over-object
/// merges shallowly (override keys win); a non-object chained input is preserved under "previous".</param>
public sealed record RunJobChainCommand(Guid ChainId, string? InputPayload = null, Guid? ChainRunId = null,
    IReadOnlyDictionary<int, string>? StepPayloadOverrides = null, int? ResumeFromStageIndex = null)
    : ICommand<ChainRunView>;

using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

/// <summary>
/// Updates the mutable configuration of an existing job definition.
/// Supports both image-based and code-based workload sources.
/// Exactly one of (MapImage, MapRuntimeId+MapSource) must be provided; they are mutually exclusive.
/// Inputs and env are opaque — PlaceContext does not interpret them.
/// </summary>
public sealed record UpdateJobCommand(
    Guid JobId,
    string Name,
    string? Description,

    // ── Map workload source — image XOR code ─────────────────────────────────────────────────────
    /// <summary>Pre-built container image (ImageWorkload). Provide this OR the Code* fields, not both.</summary>
    string? MapImage,
    /// <summary>Runtime identifier for code workload (e.g. "node", "python").</summary>
    string? MapRuntimeId,
    /// <summary>Inline source code for code workload.</summary>
    string? MapSource,
    /// <summary>Entry-point filename within the runtime work dir (optional; runtime default used when null).</summary>
    string? MapEntrypoint,

    IReadOnlyList<string> InputPayloads,
    IReadOnlyDictionary<string, string> MapEnv,

    // ── Reduce workload source (optional) — image XOR code ───────────────────────────────────────
    string? ReduceImage,
    string? ReduceRuntimeId,
    string? ReduceSource,
    string? ReduceEntrypoint,
    IReadOnlyDictionary<string, string>? ReduceEnv,

    int ConcurrencyLimit,
    IReadOnlyCollection<int> SuccessExitCodes,
    IReadOnlyCollection<int> PartialExitCodes,
    /// <summary>
    /// Opt-in: allow outbound network access from containers. Default false.
    /// When false, --network none is applied. When true, Docker's default bridge is used.
    /// </summary>
    bool AllowNetworkEgress = false,

    // ── Multi-file code workload (preferred over the single MapSource/ReduceSource strings) ─────────
    /// <summary>Map-step source files. When non-empty, supersedes <see cref="MapSource"/>.</summary>
    IReadOnlyList<CodeFileDto>? MapFiles = null,
    /// <summary>Reduce-step source files. When non-empty, supersedes <see cref="ReduceSource"/>.</summary>
    IReadOnlyList<CodeFileDto>? ReduceFiles = null,

    /// <summary>Declared input parameters prompted at run time (or injected by an event source).</summary>
    IReadOnlyList<JobParameterDto>? Parameters = null,

    /// <summary>Post-job actions: turn the run's artifacts into stored outputs (report/chart/CSV/bundle).</summary>
    IReadOnlyList<PlaceContext.Domain.ValueObjects.PostJobActionKind>? PostJobActions = null)
    : ICommand<JobView>;

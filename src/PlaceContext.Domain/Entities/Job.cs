using PlaceContext.Domain.Common;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>
/// Aggregate Root: a saved job configuration within a project. A Job is a reusable, named spec that
/// describes how to run a map/reduce workload — either via pre-built container images or inline
/// source code executed inside a generic runtime sandbox. Execution is captured as
/// <see cref="JobRun"/> history.
/// All fields are generic — no domain knowledge lives here.
/// </summary>
public sealed class Job : AggregateRoot
{
    private Job(
        Guid id,
        Guid projectId,
        string name,
        string? description,
        MapSpec mapSpec,
        ReduceSpec? reduceSpec,
        int concurrencyLimit,
        ExitCodePolicy exitCodePolicy,
        bool allowNetworkEgress,
        int timeoutSeconds,
        IReadOnlyList<JobParameter> parameters,
        IReadOnlyList<PostJobActionKind> postJobActions,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Description = description;
        MapSpec = mapSpec;
        ReduceSpec = reduceSpec;
        ConcurrencyLimit = concurrencyLimit;
        ExitCodePolicy = exitCodePolicy;
        AllowNetworkEgress = allowNetworkEgress;
        TimeoutSeconds = timeoutSeconds;
        Parameters = parameters;
        PostJobActions = postJobActions;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Default per-container timeout when a job doesn't specify one.</summary>
    public const int DefaultTimeoutSeconds = 300;

    /// <summary>Hard ceiling for a per-job timeout (1 hour) — guards against runaway containers.</summary>
    public const int MaxTimeoutSeconds = 3600;

    public Guid Id { get; }
    public Guid ProjectId { get; }

    /// <summary>Human-readable name for this job definition (unique within a project).</summary>
    public string Name { get; private set; }

    /// <summary>Optional freetext description of what this job does.</summary>
    public string? Description { get; private set; }

    /// <summary>Specification for the map (sharding) phase — one container per shard.</summary>
    public MapSpec MapSpec { get; private set; }

    /// <summary>Optional reduce step that runs after all shards complete. Null = no reduce.</summary>
    public ReduceSpec? ReduceSpec { get; private set; }

    /// <summary>Max number of map-shard containers to run concurrently. Must be >= 1.</summary>
    public int ConcurrencyLimit { get; private set; }

    /// <summary>Policy mapping raw container exit codes to shard outcomes.</summary>
    public ExitCodePolicy ExitCodePolicy { get; private set; }

    /// <summary>
    /// When true, containers for this job are allowed outbound network access (Docker bridge).
    /// When false (default), <c>--network none</c> is applied. Opt-in per job — not enabled by default.
    /// </summary>
    public bool AllowNetworkEgress { get; private set; }

    /// <summary>
    /// Per-container wall-clock timeout in seconds. A container exceeding this is killed and the shard
    /// fails. Defaults to <see cref="DefaultTimeoutSeconds"/>; raise it for long-running jobs.
    /// </summary>
    public int TimeoutSeconds { get; private set; }

    /// <summary>Declared input fields this job needs before it can run. Empty = no prompt.</summary>
    public IReadOnlyList<JobParameter> Parameters { get; private set; }

    /// <summary>
    /// Actions run after a successful/finished run to turn its artifacts into stored outputs (HTML
    /// report, chart, CSV, raw bundle) surfaced as links. Empty = no post-processing. Best-effort.
    /// </summary>
    public IReadOnlyList<PostJobActionKind> PostJobActions { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Whether this job has a reduce step.</summary>
    public bool HasReduceStep => ReduceSpec is not null;

    /// <summary>True when the job declares parameters that must be collected before a run.</summary>
    public bool RequiresInput => Parameters.Count > 0;

    // ── Factories ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Factory: creates a new job definition.</summary>
    public static Job Create(
        Guid projectId,
        string name,
        string? description,
        MapSpec mapSpec,
        ReduceSpec? reduceSpec,
        int concurrencyLimit,
        ExitCodePolicy exitCodePolicy,
        DateTimeOffset createdAt,
        bool allowNetworkEgress = false,
        IReadOnlyList<JobParameter>? parameters = null,
        int timeoutSeconds = DefaultTimeoutSeconds,
        IReadOnlyList<PostJobActionKind>? postJobActions = null)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("ProjectId must not be empty.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Job name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(mapSpec);
        ArgumentNullException.ThrowIfNull(exitCodePolicy);
        if (concurrencyLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(concurrencyLimit), "Concurrency limit must be >= 1.");
        if (mapSpec.InputPayloads.Count == 0)
            throw new ArgumentException("A job must have at least one shard (input payload).", nameof(mapSpec));

        return new Job(
            Guid.NewGuid(), projectId, name.Trim(), description?.Trim(),
            mapSpec, reduceSpec, concurrencyLimit, exitCodePolicy,
            allowNetworkEgress, NormalizeTimeout(timeoutSeconds), parameters ?? Array.Empty<JobParameter>(),
            DistinctActions(postJobActions), createdAt, createdAt);
    }

    // Post-job actions are a set (no duplicates) and order-stable for display.
    private static IReadOnlyList<PostJobActionKind> DistinctActions(IReadOnlyList<PostJobActionKind>? actions)
        => actions is null ? Array.Empty<PostJobActionKind>() : actions.Distinct().ToList();

    /// <summary>Clamps a requested timeout into the valid [1, <see cref="MaxTimeoutSeconds"/>] range.</summary>
    public static int NormalizeTimeout(int timeoutSeconds)
        => timeoutSeconds < 1 ? DefaultTimeoutSeconds : Math.Min(timeoutSeconds, MaxTimeoutSeconds);

    /// <summary>Rehydrates a job from persisted state. Infrastructure only.</summary>
    public static Job Rehydrate(
        Guid id,
        Guid projectId,
        string name,
        string? description,
        MapSpec mapSpec,
        ReduceSpec? reduceSpec,
        int concurrencyLimit,
        ExitCodePolicy exitCodePolicy,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        bool allowNetworkEgress = false,
        IReadOnlyList<JobParameter>? parameters = null,
        int timeoutSeconds = DefaultTimeoutSeconds,
        IReadOnlyList<PostJobActionKind>? postJobActions = null)
        => new(id, projectId, name, description, mapSpec, reduceSpec, concurrencyLimit, exitCodePolicy,
               allowNetworkEgress, NormalizeTimeout(timeoutSeconds), parameters ?? Array.Empty<JobParameter>(),
               DistinctActions(postJobActions), createdAt, updatedAt);

    // ── Behaviour ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the job's mutable configuration. Validates the same invariants as Create.
    /// </summary>
    public void Update(
        string name,
        string? description,
        MapSpec mapSpec,
        ReduceSpec? reduceSpec,
        int concurrencyLimit,
        ExitCodePolicy exitCodePolicy,
        DateTimeOffset updatedAt,
        bool allowNetworkEgress = false,
        IReadOnlyList<JobParameter>? parameters = null,
        int timeoutSeconds = DefaultTimeoutSeconds,
        IReadOnlyList<PostJobActionKind>? postJobActions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Job name must not be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(mapSpec);
        ArgumentNullException.ThrowIfNull(exitCodePolicy);
        if (concurrencyLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(concurrencyLimit), "Concurrency limit must be >= 1.");
        if (mapSpec.InputPayloads.Count == 0)
            throw new ArgumentException("A job must have at least one shard (input payload).", nameof(mapSpec));

        Name = name.Trim();
        Description = description?.Trim();
        MapSpec = mapSpec;
        ReduceSpec = reduceSpec;
        ConcurrencyLimit = concurrencyLimit;
        ExitCodePolicy = exitCodePolicy;
        AllowNetworkEgress = allowNetworkEgress;
        TimeoutSeconds = NormalizeTimeout(timeoutSeconds);
        if (parameters is not null) Parameters = parameters;
        if (postJobActions is not null) PostJobActions = DistinctActions(postJobActions);
        UpdatedAt = updatedAt;
    }

    /// <summary>Sets just the per-container timeout (clamped). Used by operational settings edits.</summary>
    public void SetTimeout(int timeoutSeconds, DateTimeOffset updatedAt)
    {
        TimeoutSeconds = NormalizeTimeout(timeoutSeconds);
        UpdatedAt = updatedAt;
    }
}

namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Immutable snapshot of the workload spec that was actually executed for a <see cref="PlaceContext.Domain.Entities.JobRun"/>.
/// Captured at run-start so the run history is reproducible regardless of later job edits.
/// Contains only the execution-relevant parts: the source, env, inputs, concurrency, and network
/// policy — no job metadata. All content is opaque; PlaceContext does not interpret it.
/// </summary>
public sealed class WorkloadSnapshot
{
    public WorkloadSnapshot(
        WorkloadSource mapSource,
        IReadOnlyList<string> inputPayloads,
        IReadOnlyDictionary<string, string> mapEnv,
        WorkloadSource? reduceSource,
        IReadOnlyDictionary<string, string>? reduceEnv,
        int concurrencyLimit,
        bool allowNetworkEgress = false)
    {
        ArgumentNullException.ThrowIfNull(mapSource);
        ArgumentNullException.ThrowIfNull(inputPayloads);
        ArgumentNullException.ThrowIfNull(mapEnv);
        if (concurrencyLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(concurrencyLimit));

        MapSource = mapSource;
        InputPayloads = inputPayloads;
        MapEnv = mapEnv;
        ReduceSource = reduceSource;
        ReduceEnv = reduceEnv;
        ConcurrencyLimit = concurrencyLimit;
        AllowNetworkEgress = allowNetworkEgress;
    }

    /// <summary>The workload source (image or code) that was run for each map shard.</summary>
    public WorkloadSource MapSource { get; }

    /// <summary>Opaque JSON payloads — one per shard — as they were at run-start.</summary>
    public IReadOnlyList<string> InputPayloads { get; }

    /// <summary>Environment variables injected into each map shard container.</summary>
    public IReadOnlyDictionary<string, string> MapEnv { get; }

    /// <summary>Workload source for the reduce step; null = no reduce step ran.</summary>
    public WorkloadSource? ReduceSource { get; }

    /// <summary>Environment variables injected into the reduce container; null = no reduce step.</summary>
    public IReadOnlyDictionary<string, string>? ReduceEnv { get; }

    /// <summary>Max concurrent map shards as configured at run-start.</summary>
    public int ConcurrencyLimit { get; }

    /// <summary>Whether outbound network access was permitted for this run's containers.</summary>
    public bool AllowNetworkEgress { get; }

    /// <summary>Factory: snapshot from a job's current specs.</summary>
    public static WorkloadSnapshot From(
        MapSpec mapSpec,
        ReduceSpec? reduceSpec,
        int concurrencyLimit,
        bool allowNetworkEgress = false)
        => new(
            mapSpec.Source,
            mapSpec.InputPayloads,
            mapSpec.Env,
            reduceSpec?.Source,
            reduceSpec?.Env,
            concurrencyLimit,
            allowNetworkEgress);
}

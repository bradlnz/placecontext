namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Value Object: the specification for the map (fan-out) phase of a job.
/// Each entry in <see cref="InputPayloads"/> is an opaque JSON string passed on STDIN to one shard.
/// The workload source and env keys are generic — PlaceContext does not interpret them.
/// </summary>
public sealed class MapSpec
{
    /// <summary>Create a MapSpec backed by a pre-built image.</summary>
    public MapSpec(string image, IReadOnlyList<string> inputPayloads, IReadOnlyDictionary<string, string> env)
        : this(new WorkloadSource.ImageWorkload(image), inputPayloads, env) { }

    /// <summary>Create a MapSpec with an explicit WorkloadSource (image or code).</summary>
    public MapSpec(WorkloadSource source, IReadOnlyList<string> inputPayloads, IReadOnlyDictionary<string, string> env)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(inputPayloads);
        ArgumentNullException.ThrowIfNull(env);

        Source = source;
        InputPayloads = inputPayloads;
        Env = env;
    }

    /// <summary>How to run each shard — either a pre-built image or inline source code.</summary>
    public WorkloadSource Source { get; }

    /// <summary>
    /// Ordered list of opaque JSON strings — one per shard. Each is written to the container's STDIN.
    /// The count determines the number of shards.
    /// </summary>
    public IReadOnlyList<string> InputPayloads { get; }

    /// <summary>Environment variable key/value pairs injected into each shard container.</summary>
    public IReadOnlyDictionary<string, string> Env { get; }

    /// <summary>Number of shards this spec will produce.</summary>
    public int ShardCount => InputPayloads.Count;

    // ── Convenience accessors (backward compat) ───────────────────────────────────────────────────

    /// <summary>Image name when source is an ImageWorkload; null otherwise.</summary>
    public string? Image => (Source as WorkloadSource.ImageWorkload)?.Image;
}

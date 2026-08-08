namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Value Object: the optional reduce step that runs after all map shards have completed.
/// The reduce container receives the shard artifacts mounted read-only and writes its own artifact.
/// All fields are generic — PlaceContext does not interpret the source, env, or artifacts.
/// </summary>
public sealed class ReduceSpec
{
    /// <summary>Create a ReduceSpec backed by a pre-built image.</summary>
    public ReduceSpec(string image, IReadOnlyDictionary<string, string> env)
        : this(new ImageWorkload(image), env) { }

    /// <summary>Create a ReduceSpec with an explicit WorkloadSource (image or code).</summary>
    public ReduceSpec(WorkloadSource source, IReadOnlyDictionary<string, string> env)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(env);

        Source = source;
        Env = env;
    }

    /// <summary>How to run the reduce step — either a pre-built image or inline source code.</summary>
    public WorkloadSource Source { get; }

    /// <summary>Environment variable key/value pairs injected into the reduce container.</summary>
    public IReadOnlyDictionary<string, string> Env { get; }

    // ── Convenience accessor (backward compat) ───────────────────────────────────────────────────

    /// <summary>Image name when source is an ImageWorkload; null otherwise.</summary>
    public string? Image => (Source as ImageWorkload)?.Image;
}

namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Discriminated union: describes how a workload step (map shard or reduce) is executed.
/// Either a pre-built container image or inline source code to be run inside a generic runtime sandbox.
/// All fields are generic — no domain knowledge lives here.
/// </summary>
public abstract class WorkloadSource
{
    protected WorkloadSource() { }

    /// <summary>Returns the display label for the source (image name or runtimeId).</summary>
    public abstract string Label { get; }

}

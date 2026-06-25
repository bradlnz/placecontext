namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// Value Object: maps raw container exit codes to shard outcomes.
/// PlaceContext never hardcodes specific nonzero exit codes — the policy is per-job configuration.
/// Default: success = {0}, partial = {}, all other codes = failed.
/// </summary>
public sealed class ExitCodePolicy
{
    /// <summary>Default policy: exit 0 = succeeded; everything else = failed.</summary>
    public static readonly ExitCodePolicy Default = new(successCodes: new[] { 0 }, partialCodes: Array.Empty<int>());

    public ExitCodePolicy(IReadOnlyCollection<int> successCodes, IReadOnlyCollection<int> partialCodes)
    {
        ArgumentNullException.ThrowIfNull(successCodes);
        ArgumentNullException.ThrowIfNull(partialCodes);

        SuccessCodes = successCodes;
        PartialCodes = partialCodes;
    }

    /// <summary>Exit codes that map to <see cref="WorkloadOutcome.Succeeded"/>.</summary>
    public IReadOnlyCollection<int> SuccessCodes { get; }

    /// <summary>Exit codes that map to <see cref="WorkloadOutcome.Partial"/>. May be empty.</summary>
    public IReadOnlyCollection<int> PartialCodes { get; }

    /// <summary>Classifies a raw exit code into a shard outcome.</summary>
    public WorkloadOutcome Classify(int exitCode)
    {
        if (SuccessCodes.Contains(exitCode)) return WorkloadOutcome.Succeeded;
        if (PartialCodes.Contains(exitCode)) return WorkloadOutcome.Partial;
        return WorkloadOutcome.Failed;
    }
}

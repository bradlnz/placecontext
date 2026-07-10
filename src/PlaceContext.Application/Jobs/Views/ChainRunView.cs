namespace PlaceContext.Application.Dtos;

/// <summary>
/// The result of running a chain: one entry per executed step (steps after a failure never run),
/// plus the final payload — the last step's primary output, i.e. what the chain produced.
/// </summary>
public sealed record ChainRunView(
    Guid ChainId,
    string ChainName,
    string Status,
    IReadOnlyList<ChainStepRunView> Steps,
    string? FinalOutput);

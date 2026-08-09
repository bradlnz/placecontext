namespace PlaceContext.Data.Integration;

public sealed record DataJobCatalog(
    IReadOnlyList<DataJobSummary> Jobs,
    IReadOnlyList<DataChainSummary> Chains,
    IReadOnlyList<DataRunSummary> Runs);

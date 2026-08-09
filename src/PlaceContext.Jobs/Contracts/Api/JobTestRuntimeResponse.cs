namespace PlaceContext.Jobs.Contracts.Api;

public sealed record JobTestRuntimeResponse(
    string Id,
    string Label,
    string FrameworkLabel,
    string Entrypoint,
    IReadOnlyList<JobTestCodeFileResponse> StarterFiles);

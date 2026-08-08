namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record JobTestRuntimeResponse(
    string Id,
    string Label,
    string FrameworkLabel,
    string Entrypoint,
    IReadOnlyList<JobTestCodeFileResponse> StarterFiles);

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record UpdateJobTestCodeRequest(
    string RuntimeId,
    string? Entrypoint,
    IReadOnlyList<JobTestCodeFileResponse> CodeFiles);

namespace PlaceContext.Jobs.Contracts.Api;

public sealed record UpdateJobTestCodeRequest(
    string RuntimeId,
    string? Entrypoint,
    IReadOnlyList<JobTestCodeFileResponse> CodeFiles);

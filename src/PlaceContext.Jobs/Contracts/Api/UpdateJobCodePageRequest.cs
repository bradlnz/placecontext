using PlaceContext.Jobs.Contracts.Management;

namespace PlaceContext.Jobs.Contracts.Api;

public sealed record UpdateJobCodePageRequest(
    string RuntimeId,
    string? Entrypoint,
    IReadOnlyList<JobCodeFile> Files);

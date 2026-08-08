using PlaceContext.Host.Api;

namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record UpdateJobCodePageRequest(
    string RuntimeId,
    string? Entrypoint,
    IReadOnlyList<JobCodeFile> Files);

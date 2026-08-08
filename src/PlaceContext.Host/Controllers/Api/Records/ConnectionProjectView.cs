namespace PlaceContext.Host.Controllers.Api.Records;

public sealed record ConnectionProjectView(
    Guid Id,
    string Name,
    bool HasExternalDatabase,
    bool HasExternalIndex);

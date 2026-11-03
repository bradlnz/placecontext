namespace PlaceContext.Application.Jobs;

public sealed record JobDataGrant(Guid TenantId, Guid ProjectId, Guid JobId, Guid RunId, DateTimeOffset ExpiresAt);

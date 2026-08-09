using PlaceContext.Application.Ports;

namespace PlaceContext.Data.Analytics;

internal sealed record AnalyticsRefreshRequest(
    TenantContext Tenant,
    Guid ProjectId,
    Guid OperationId,
    string? TableName,
    string? Instruction);

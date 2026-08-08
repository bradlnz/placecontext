using System.Collections.Concurrent;
using System.Threading.Channels;
using PlaceContext.Infrastructure.Operations;
using PlaceContext.Infrastructure.Tenancy;

namespace PlaceContext.Infrastructure.Scheduling;

/// <summary>One queued chart job: the whole project (TableName null) or a single table.</summary>
public sealed record AnalyticsRefreshRequest(
    TenantInfo Tenant, Guid ProjectId, Guid OpId, string? TableName, string? Instruction);

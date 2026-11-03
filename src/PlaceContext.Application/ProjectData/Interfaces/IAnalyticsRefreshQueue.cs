using PlaceContext.Application.Tenancy;

namespace PlaceContext.Application.ProjectData;

public interface IAnalyticsRefreshQueue
{
    bool TryEnqueue(TenantInfo tenant, Guid projectId, string projectName = "project", string? tableName = null, string? instruction = null);
    bool IsPending(Guid projectId);
    bool IsPending(Guid projectId, string? tableName);
}

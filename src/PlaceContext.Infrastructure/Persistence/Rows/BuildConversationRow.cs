using PlaceContext.Application.Build;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class BuildConversationRow : ITenantOwned
{
    public Guid Id
    {
        get; set;
    }
    public Guid TenantId
    {
        get; set;
    }
    public Guid UserId
    {
        get; set;
    }
    public Guid ProjectId
    {
        get; set;
    }
    public string Content { get; set; } = "";
    public string Title { get; set; } = "";
    public string? ShareTokenHash { get; set; }
    public DateTimeOffset? ShareExpiresAt { get; set; }
    public string? SharedContent { get; set; }
    public DateTimeOffset UpdatedAt
    {
        get; set;
    }
    public Guid Version
    {
        get; set;
    }
    public BuildRunStatus Status
    {
        get; set;
    }
    public bool StopRequested
    {
        get; set;
    }
    public Guid? Lease
    {
        get; set;
    }
    public DateTimeOffset? LeaseUntil
    {
        get; set;
    }
}

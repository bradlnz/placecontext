namespace PlaceContext.Infrastructure.Persistence;

public sealed class BuildMemoryRow : ITenantOwned
{
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
}

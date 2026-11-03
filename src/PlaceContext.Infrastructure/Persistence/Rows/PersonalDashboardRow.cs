namespace PlaceContext.Infrastructure.Persistence;

public sealed class PersonalDashboardRow : ITenantOwned
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
    public string Definition { get; set; } = "";
    public Guid Version
    {
        get; set;
    }
}

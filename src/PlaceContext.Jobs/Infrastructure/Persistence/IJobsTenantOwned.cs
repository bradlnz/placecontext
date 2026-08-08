namespace PlaceContext.Jobs.Infrastructure.Persistence;

internal interface IJobsTenantOwned
{
    Guid TenantId { get; set; }
}

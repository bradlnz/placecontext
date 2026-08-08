namespace PlaceContext.Crm.Infrastructure.Persistence;

internal interface ICrmTenantOwned
{
    Guid TenantId { get; set; }
}

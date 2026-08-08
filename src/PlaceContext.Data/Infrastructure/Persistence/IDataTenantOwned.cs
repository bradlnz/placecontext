namespace PlaceContext.Data.Infrastructure.Persistence;

internal interface IDataTenantOwned
{
    Guid TenantId { get; set; }
}

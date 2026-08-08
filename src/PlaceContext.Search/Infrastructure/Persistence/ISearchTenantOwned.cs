namespace PlaceContext.Search.Infrastructure.Persistence;

internal interface ISearchTenantOwned
{
    Guid TenantId { get; set; }
}

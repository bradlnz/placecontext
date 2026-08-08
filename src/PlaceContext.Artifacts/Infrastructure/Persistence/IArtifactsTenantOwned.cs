namespace PlaceContext.Artifacts.Infrastructure.Persistence;

internal interface IArtifactsTenantOwned
{
    Guid TenantId { get; set; }
}

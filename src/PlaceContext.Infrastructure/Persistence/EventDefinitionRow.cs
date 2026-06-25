namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF Core row for a user-defined <see cref="PlaceContext.Domain.Entities.EventDefinition"/>.</summary>
public sealed class EventDefinitionRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? PayloadSchema { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

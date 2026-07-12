namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.DataEntity"/>.</summary>
public sealed class DataEntityRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string TableName { get; set; } = "";
    public string? LabelColumn { get; set; }
    /// <summary>JSON array of {Column, TargetEntity, TargetColumn} relations.</summary>
    public string RelationsJson { get; set; } = "[]";
    /// <summary>JSON array of literal tag strings this entity also maps against.</summary>
    public string TagsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

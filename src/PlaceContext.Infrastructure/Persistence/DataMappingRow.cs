namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.DataMapping"/>.</summary>
public sealed class DataMappingRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid JobId { get; set; }
    public string TargetTable { get; set; } = "";
    /// <summary>Dot-path to the record array inside the run's primary artifact. Null = root.</summary>
    public string? RowsPath { get; set; }
    /// <summary>JSON array of {SourcePath, Column, Type} field mappings.</summary>
    public string FieldsJson { get; set; } = "[]";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

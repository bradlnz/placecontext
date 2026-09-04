using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF row for one record-link occurrence (table record_links).</summary>
public sealed class RecordLinkRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Kind { get; set; } = "";
    public string NormalizedValue { get; set; } = "";
    public string DisplayValue { get; set; } = "";
    public string TableName { get; set; } = "";
    public string ColumnName { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

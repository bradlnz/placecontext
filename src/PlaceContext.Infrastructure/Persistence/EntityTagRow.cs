using Microsoft.EntityFrameworkCore;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.Persistence;

/// <summary>Flat EF row for one run ⇄ entity-key tag (table entity_tags).</summary>
public sealed class EntityTagRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid EntityId { get; set; }
    public string EntityName { get; set; } = "";
    public string Key { get; set; } = "";
    public Guid RunId { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

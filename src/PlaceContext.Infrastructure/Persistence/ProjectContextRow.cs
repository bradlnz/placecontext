using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class ProjectContextRow : ITenantOwned
{
    public Guid ProjectId { get; set; }
    public Guid TenantId { get; set; }
    public string Markdown { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

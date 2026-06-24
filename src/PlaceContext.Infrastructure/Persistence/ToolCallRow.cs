using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class ToolCallRow : ITenantOwned
{
    public string Id { get; set; } = "";
    public Guid TenantId { get; set; }
    public string Tool { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Project { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "";
    public long DurationMs { get; set; }
    public string RequestJson { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

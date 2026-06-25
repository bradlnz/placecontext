using PlaceContext.Application.Ports;
using Microsoft.EntityFrameworkCore;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class ActivityRecordRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public int Sequence { get; set; }
    public string Summary { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string AuthorKind { get; set; } = "";
    public string Rationale { get; set; } = "";
    public int TestsAdded { get; set; }
    public int TestsRemoved { get; set; }
    public int TestsChanged { get; set; }
    public int RiskResolved { get; set; }
    public int RiskIntroduced { get; set; }
    public bool ArchReviewed { get; set; }
    public bool LiveVerified { get; set; }
    public string TouchedFiles { get; set; } = "[]";
    public string TouchedNodes { get; set; } = "[]";
    public string? CommitSha { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

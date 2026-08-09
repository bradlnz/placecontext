namespace PlaceContext.Agents.Infrastructure.Cluster;

internal sealed class TokenEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TokenPrefix { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

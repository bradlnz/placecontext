namespace PlaceContext.Infrastructure.Persistence;

/// <summary>
/// Flat EF Core row for a <see cref="PlaceContext.Domain.Entities.AgentConfig"/>.
/// One per project — singleton config for the chat agent.
/// </summary>
public sealed class AgentConfigRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string BaseModel { get; set; } = "qwen3.5:0.8b";
    public string SystemPrompt { get; set; } = "";
    public int MaxContextChunks { get; set; } = 5;
    public float Temperature { get; set; } = 0.7f;
    public float TopP { get; set; } = 0.9f;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

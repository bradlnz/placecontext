using PlaceContext.Application.Build;

namespace PlaceContext.Infrastructure.Persistence;

public sealed class ModelConnectionRow : ITenantOwned
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Protocol { get; set; } = "openai";
    public string BaseUrl { get; set; } = "";
    public string ModelsJson { get; set; } = "[]";
    public string ApiKey { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool AutoDiscovered { get; set; }
}

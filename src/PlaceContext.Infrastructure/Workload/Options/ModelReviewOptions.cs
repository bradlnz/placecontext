namespace PlaceContext.Infrastructure.Workload;

public sealed class ModelReviewOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "jev-1.14.0";
    public int RequestsPerSecond { get; set; }
    public int TokensPerSecond { get; set; }
    public string RateLimitScope { get; set; } = "typesafe-organization";
}

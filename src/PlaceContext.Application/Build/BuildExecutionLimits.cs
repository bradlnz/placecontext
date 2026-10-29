namespace PlaceContext.Application.Build;

public static class BuildExecutionLimits
{
    public static readonly TimeSpan ModelResponse = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan Turn = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan Pulse = TimeSpan.FromSeconds(10);
}

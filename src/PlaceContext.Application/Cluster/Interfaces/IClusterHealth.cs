namespace PlaceContext.Application.Cluster;

public interface IClusterHealth
{
    IReadOnlyList<string> HealthyEndpoints
    {
        get;
    }
}

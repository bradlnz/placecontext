namespace PlaceContext.Application.Ports;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

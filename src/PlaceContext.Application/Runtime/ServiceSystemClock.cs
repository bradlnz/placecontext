using PlaceContext.Application.Ports;

namespace PlaceContext.Application.Runtime;

public sealed class ServiceSystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

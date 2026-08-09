using PlaceContext.Application.Ports;

namespace PlaceContext.ServiceDefaults;

public sealed class ServiceSystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

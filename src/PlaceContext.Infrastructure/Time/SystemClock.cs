using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure;

/// <summary>The real wall clock. The only place <see cref="DateTimeOffset.UtcNow"/> is read.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

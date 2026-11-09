using PlaceContext.Application.Shared;

namespace PlaceContext.TestSupport;

/// <summary>A clock pinned to a fixed instant for deterministic tests.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => UtcNow = now;
    public DateTimeOffset UtcNow
    {
        get; set;
    }
}

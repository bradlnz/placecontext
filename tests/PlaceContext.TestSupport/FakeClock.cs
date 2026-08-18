using System.Collections.Concurrent;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Entities;
using PlaceContext.Domain.Repositories;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.TestSupport;

/// <summary>A clock pinned to a fixed instant for deterministic tests.</summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) => UtcNow = now;
    public DateTimeOffset UtcNow { get; set; }
}

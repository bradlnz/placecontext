using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Shared;

/// <summary>Deterministic time source (port). Infrastructure supplies the system clock.</summary>
public interface IClock
{
    DateTimeOffset UtcNow
    {
        get;
    }
}

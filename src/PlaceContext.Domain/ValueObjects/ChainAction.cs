using System.Net.Mail;

namespace PlaceContext.Domain.ValueObjects;

/// <summary>A typed non-job operation that can occupy one chain stage.</summary>
public abstract class ChainAction
{
    public abstract string Type { get; }
    public abstract string DisplayName { get; }
}

using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Entities;

/// <summary>Which trust gates a change passed. Both default to false (un-trusted until proven).</summary>
public readonly record struct ActivityVerification(bool ArchitectureReviewerRun, bool LiveVerified)
{
    public static readonly ActivityVerification None = new(false, false);
}

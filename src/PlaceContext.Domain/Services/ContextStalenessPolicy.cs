using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Domain.Services;

/// <summary>
/// Pure domain policy: a context note is stale when the code it describes has changed underneath it
/// — either committed more recently than the note was last revised, or its digest no longer matches
/// what the note was anchored to. Stale context is an agentic-debt contributor.
/// </summary>
public sealed class ContextStalenessPolicy
{
    public bool IsStale(ContextNote note, DateTimeOffset codeLastModifiedUtc, ContentDigest currentDigest)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(currentDigest);

        return codeLastModifiedUtc > note.UpdatedAt
            || note.DescribedCodeDigest != currentDigest;
    }
}

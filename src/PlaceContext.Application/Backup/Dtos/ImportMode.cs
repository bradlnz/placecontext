namespace PlaceContext.Application.Dtos;

/// <summary>Which entities a natural-key match creates vs. updates. Merge is the only mode today:
/// create what's missing, update what matches, never duplicate. Reserved for future modes (e.g. a
/// clean "replace" that first archives anything not in the manifest).</summary>
public enum ImportMode
{
    Merge,
}

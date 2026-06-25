namespace PlaceContext.Licensing.Licensing;

/// <summary>The catalogue of issued licenses, keyed by activation key (case-insensitive). Seeded from
/// configuration in this stub; a real licensing service would back this with a database.</summary>
public sealed class LicenseStore
{
    private readonly Dictionary<string, License> _byKey;

    public LicenseStore(IEnumerable<License> licenses)
        => _byKey = licenses.ToDictionary(l => l.Key, StringComparer.OrdinalIgnoreCase);

    public License? Find(string key)
        => _byKey.TryGetValue(key, out var license) ? license : null;
}

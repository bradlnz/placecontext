namespace PlaceContext.Domain.ValueObjects;

/// <summary>How a change moved the test suite. Counts are non-negative.</summary>
public readonly record struct TestDelta(int Added, int Removed, int Changed)
{
    public static readonly TestDelta None = new(0, 0, 0);

    public static TestDelta From(int added, int removed, int changed)
    {
        if (added < 0 || removed < 0 || changed < 0)
            throw new ArgumentException("TestDelta counts must be non-negative.");

        return new TestDelta(added, removed, changed);
    }

    /// <summary>True when the change touched the test suite at all — a process-trust signal.</summary>
    public bool HasTestActivity => Added > 0 || Removed > 0 || Changed > 0;

    public int Net => Added - Removed;
}

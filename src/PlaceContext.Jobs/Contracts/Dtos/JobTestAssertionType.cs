namespace PlaceContext.Application.Dtos;

/// <summary>The assertion applied to a job's primary output after a successful run.</summary>
public enum JobTestAssertionType
{
    Succeeds,
    OutputEquals,
    OutputContains,
    JsonSubset,
}

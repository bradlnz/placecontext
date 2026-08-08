namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One column of a <see cref="Entities.DataMapping"/>: the value at <paramref name="SourcePath"/>
/// (a dot-path into an ingested record, e.g. "price" or "meta.city") lands in target column
/// <paramref name="Column"/> of the declared <paramref name="Type"/> (a project-data column type
/// such as "text", "integer", "numeric", "boolean", "timestamptz"). Use <c>$</c> to map the
/// complete current record or scalar result. Objects and arrays are preserved as JSON text in the
/// declared column, so nested payloads remain usable without uncontrolled schema expansion.
/// </summary>
public sealed record DataFieldMapping(string SourcePath, string Column, string Type)
{
    public DataFieldMapping ThrowIfInvalid()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            throw new ArgumentException("A field mapping needs a source path.");
        if (string.IsNullOrWhiteSpace(Column))
            throw new ArgumentException("A field mapping needs a target column.");
        if (string.IsNullOrWhiteSpace(Type))
            throw new ArgumentException("A field mapping needs a column type.");
        return this;
    }
}

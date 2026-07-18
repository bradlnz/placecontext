namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One column of a <see cref="Entities.DataMapping"/>: the value at <paramref name="SourcePath"/>
/// (a dot-path into an ingested record, e.g. "price" or "meta.city") lands in target column
/// <paramref name="Column"/> of the declared <paramref name="Type"/> (a project-data column type
/// such as "text", "integer", "numeric", "boolean", "timestamptz"). When the value is a JSON
/// object it is flattened instead: each nested leaf gets its own inferred-type column named
/// <c>{Column}_{path}</c> (e.g. "meta.region" → "meta_region"), and the declared column is only
/// written when a non-object value (scalar, array, empty object) shows up for the field.
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

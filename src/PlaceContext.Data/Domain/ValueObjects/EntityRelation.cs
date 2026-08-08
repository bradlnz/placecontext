namespace PlaceContext.Domain.ValueObjects;

/// <summary>
/// One relation of a <see cref="Entities.DataEntity"/>: the value in <paramref name="Column"/>
/// matches <paramref name="TargetColumn"/> on the entity named <paramref name="TargetEntity"/> —
/// e.g. listings.suburb ↔ sites.suburb.
/// </summary>
public sealed record EntityRelation(string Column, string TargetEntity, string TargetColumn);

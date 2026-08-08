namespace PlaceContext.Application.Features;

/// <summary>One relation edge of the entity graph: this column ↔ that entity's column.</summary>
public sealed record EntityRelationDto(string Column, string TargetEntity, string TargetColumn);

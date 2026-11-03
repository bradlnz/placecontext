namespace PlaceContext.Application.Dtos;

/// <summary>One column of a materialised index: the field name and its OpenSearch mapping type.</summary>
public sealed record SearchMappingField(string Name, string OpenSearchType);

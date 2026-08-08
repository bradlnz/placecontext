namespace PlaceContext.Application.Dtos;

public sealed record OpenSearchFieldView(
    string Name, string Type, bool Searchable, bool Aggregatable);

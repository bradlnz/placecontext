namespace PlaceContext.Application.Dtos;

public sealed record SearchFieldView(
    string Name, string Type, bool Searchable, bool Aggregatable);

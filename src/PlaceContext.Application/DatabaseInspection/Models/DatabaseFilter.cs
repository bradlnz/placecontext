namespace PlaceContext.Application.DatabaseInspection;

public sealed record DatabaseFilter(string Column, string Operator, string? Value);

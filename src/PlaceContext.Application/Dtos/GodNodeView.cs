using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record GodNodeView(string Id, string Label, int Degree);

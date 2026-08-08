using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public abstract record ChainActionView(string Type, string DisplayName);

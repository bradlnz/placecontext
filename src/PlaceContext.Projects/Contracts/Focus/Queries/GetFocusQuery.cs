using PlaceContext.Application.Cqrs;
using PlaceContext.Application.Dtos;

namespace PlaceContext.Application.Features;

public sealed record GetFocusQuery(int Limit = 12) : IQuery<FocusView>;

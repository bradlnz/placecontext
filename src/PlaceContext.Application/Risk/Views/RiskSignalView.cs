using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record RiskSignalView(string Code, string Severity, string Evidence);

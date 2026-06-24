using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Dtos;

public sealed record DebtSignalView(string Code, string Severity, string Evidence);

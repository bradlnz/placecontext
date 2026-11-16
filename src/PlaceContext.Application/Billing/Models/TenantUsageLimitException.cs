namespace PlaceContext.Application.Billing;

public sealed class TenantUsageLimitException(string message) : UnauthorizedAccessException(message);

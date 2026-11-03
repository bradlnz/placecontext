namespace PlaceContext.Application.Billing;

public sealed record WorkspaceSignupStart(bool RequiresVerification, string? ExchangeCode = null,
    string? Slug = null, string? Plan = null);

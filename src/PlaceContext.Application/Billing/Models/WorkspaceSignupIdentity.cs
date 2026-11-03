namespace PlaceContext.Application.Billing;

public sealed record WorkspaceSignupIdentity(Guid TenantId, Guid UserId, string Email,
    string DisplayName, string Plan);

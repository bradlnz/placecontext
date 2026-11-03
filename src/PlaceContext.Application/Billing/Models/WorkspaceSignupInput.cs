namespace PlaceContext.Application.Billing;

public sealed record WorkspaceSignupInput(
    string WorkspaceName, string Slug, string Email, string DisplayName,
    string Password, string ConfirmPassword, string Plan);

namespace PlaceContext.Application.Billing;

/// <summary>Self-service registration is disabled unless explicitly enabled on a managed
/// installation that also refuses implicit tenant provisioning.</summary>
public sealed class WorkspaceSignupOptions
{
    public bool Enabled { get; set; }
    /// <summary>Only permitted in Development for isolated browser tests. Never in production.</summary>
    public bool AllowUnverifiedDevelopmentSignup { get; set; }
    public string PostmarkServerToken { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "PlaceContext";
}

public sealed record WorkspaceSignupInput(
    string WorkspaceName, string Slug, string Email, string DisplayName,
    string Password, string ConfirmPassword, string Plan);

public sealed record WorkspaceSignupStart(bool RequiresVerification, string? ExchangeCode = null,
    string? Slug = null, string? Plan = null);

public sealed record WorkspaceSignupActivation(string Slug, string ExchangeCode);

public sealed record WorkspaceSignupIdentity(Guid TenantId, Guid UserId, string Email,
    string DisplayName, string Plan);

public interface IWorkspaceSignupEmailSender
{
    bool IsConfigured { get; }
    Task SendVerificationAsync(string email, string displayName, Uri confirmationLink,
        CancellationToken ct = default);
}

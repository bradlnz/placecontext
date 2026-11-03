namespace PlaceContext.Application.Billing;

/// <summary>Self-service registration is disabled unless explicitly enabled on a managed
/// installation that also refuses implicit tenant provisioning.</summary>
public sealed class WorkspaceSignupOptions
{
    public bool Enabled
    {
        get; set;
    }
    /// <summary>Only permitted in Development for isolated browser tests. Never in production.</summary>
    public bool AllowUnverifiedDevelopmentSignup
    {
        get; set;
    }
    public string PostmarkServerToken { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "PlaceContext";
}

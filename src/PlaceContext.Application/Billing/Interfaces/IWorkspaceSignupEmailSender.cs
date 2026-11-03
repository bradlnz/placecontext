namespace PlaceContext.Application.Billing;

public interface IWorkspaceSignupEmailSender
{
    bool IsConfigured
    {
        get;
    }
    Task SendVerificationAsync(string email, string displayName, Uri confirmationLink,
        CancellationToken ct = default);
}

using PlaceContext.Application.Billing;
namespace PlaceContext.Application.Auth;

public interface IWorkspaceSignup
{
    bool Enabled
    {
        get;
    }
    Task<WorkspaceSignupStart> StartAsync(WorkspaceSignupInput input, Uri publicOrigin, CancellationToken ct = default);
    Task<WorkspaceSignupActivation> VerifyAsync(string token, CancellationToken ct = default);
    Task<WorkspaceSignupIdentity> RedeemAsync(string code, Guid tenantId, CancellationToken ct = default);
}

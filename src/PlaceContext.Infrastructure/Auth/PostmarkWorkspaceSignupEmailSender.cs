using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using PlaceContext.Application.Billing;

namespace PlaceContext.Infrastructure.Auth;

/// <summary>Platform-owned verification mailer. Does not depend on a tenant or allow
/// user-provided mail endpoints, which is necessary before a workspace exists.</summary>
public sealed class PostmarkWorkspaceSignupEmailSender(
    IHttpClientFactory clients, IOptions<WorkspaceSignupOptions> options) : IWorkspaceSignupEmailSender
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.PostmarkServerToken)
        && !string.IsNullOrWhiteSpace(options.Value.FromEmail);

    public async Task SendVerificationAsync(string email, string displayName, Uri confirmationLink,
        CancellationToken ct = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Signup verification email is not configured.");
        if (confirmationLink.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Public verification links require HTTPS.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.postmarkapp.com/email");
        request.Headers.TryAddWithoutValidation("X-Postmark-Server-Token", options.Value.PostmarkServerToken);
        request.Content = JsonContent.Create(new Dictionary<string, string>
        {
            ["From"] = string.IsNullOrWhiteSpace(options.Value.FromName)
                ? options.Value.FromEmail
                : $"{options.Value.FromName} <{options.Value.FromEmail}>",
            ["To"] = email,
            ["Subject"] = "Confirm your PlaceContext workspace",
            ["TextBody"] = $"Hello {displayName},\n\nConfirm your new PlaceContext workspace by visiting:\n"
                + confirmationLink.AbsoluteUri + "\n\nThis link expires in 30 minutes. "
                + "If you didn't request it, ignore this email.",
            ["MessageStream"] = "outbound",
            ["Tag"] = "workspace-signup"
        });
        using var response = await clients.CreateClient("signup-mailer").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Workspace verification email could not be delivered.");
    }
}

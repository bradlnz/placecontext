using PlaceContext.Domain.Entities;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Application.Ports;

/// <summary>GitHub OAuth + REST gateway: authorize URL, code→token exchange, the user, and their repos.</summary>
public interface IGitHubGateway
{
    bool IsConfigured { get; }
    string BuildAuthorizeUrl(string redirectUri, string state);
    Task<string?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);
    Task<(string Login, string Name)?> GetUserAsync(string accessToken, CancellationToken ct = default);
    Task<IReadOnlyList<GitHubRepo>> ListReposAsync(string accessToken, CancellationToken ct = default);
}

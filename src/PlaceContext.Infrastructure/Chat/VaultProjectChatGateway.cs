using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.Repositories;

namespace PlaceContext.Infrastructure.Chat;

/// <summary>Uses the project Vault's LLM_API_TOKEN when present; otherwise delegates to the local cluster gateway.</summary>
public sealed class VaultProjectChatGateway(
    IHttpClientFactory http,
    IChatGateway local,
    IProjectSecretRepository secrets,
    ISecretProtector protector,
    IConfiguration configuration) : IProjectChatGateway
{
    public const string TokenSecretName = "LLM_API_TOKEN";
    private const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-4.1-mini";

    public async Task<ProjectChatStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        var token = await ResolveTokenAsync(projectId, ct);
        if (!string.IsNullOrWhiteSpace(token))
            return new ProjectChatStatus(ProjectChatBackend.ExternalLlm, true, "External LLM · Vault token");
        return local.IsEnabled
            ? new ProjectChatStatus(ProjectChatBackend.LocalCluster, true, "Local agent cluster")
            : new ProjectChatStatus(ProjectChatBackend.None, false, "No model configured");
    }

    public async Task<string> ChatAsync(
        Guid projectId,
        IReadOnlyList<ChatMessage> messages,
        ChatSettings? settings = null,
        CancellationToken ct = default)
    {
        var token = await ResolveTokenAsync(projectId, ct);
        if (string.IsNullOrWhiteSpace(token))
            return await local.ChatAsync(messages, settings, ct);

        var endpoint = configuration["PlaceContext:ExternalLlm:Endpoint"]?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = DefaultEndpoint;
        var model = configuration["PlaceContext:ExternalLlm:Model"]?.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = settings?.Model?.Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = DefaultModel;

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            model,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            temperature = settings?.Temperature,
            top_p = settings?.TopP,
            max_tokens = settings?.MaxTokens,
        });

        var client = http.CreateClient();
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (document.RootElement.TryGetProperty("choices", out var choices)
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
            return content.GetString() ?? string.Empty;
        return string.Empty;
    }

    private async Task<string?> ResolveTokenAsync(Guid projectId, CancellationToken ct)
    {
        var ciphers = await secrets.GetCiphersAsync(projectId, ct);
        if (!ciphers.TryGetValue(TokenSecretName, out var cipher) || string.IsNullOrWhiteSpace(cipher))
            return null;
        return protector.Unprotect(cipher).Trim();
    }
}
